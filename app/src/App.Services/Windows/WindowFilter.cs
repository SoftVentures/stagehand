using System.Collections.Immutable;
using App.Core.Stage;
using App.Core.Windows;
using App.Interop;
using App.Services.Settings;
using Microsoft.Extensions.Logging;

namespace App.Services.Windows;

/// <summary>
/// Policy that decides whether a given <see cref="WindowSnapshot"/> represents a
/// window Stagehand can manage. Implements the six filter rules from Plan 02
/// §Design.3 in a fixed order so results are deterministic across rebuilds.
/// </summary>
/// <remarks>
/// The filter is a pure function of <see cref="WindowSnapshot"/>. All raw Win32
/// state required for the decision (visibility, cloaked flag, top-level status,
/// styles, owner presence, process name) is expected to be pre-populated on the
/// snapshot by <c>WindowEnumerator</c>; the filter never re-enters native code.
/// <para>
/// Rule 6 depends on user-configurable state (<see cref="ISettingsService.Current"/>
/// → <see cref="BehaviorSettings.ExcludedApps"/>). The filter subscribes to
/// <see cref="ISettingsService.Changed"/> and invalidates its cached case-insensitive
/// lookup set whenever settings are persisted. Reads are thread-safe via a
/// <see langword="volatile"/> reference swap; writes are serialised on a monitor.
/// </para>
/// </remarks>
public sealed class WindowFilter : IWindowFilter, IDisposable
{
    private static readonly Action<ILogger, Exception?> LogSettingsChanged = LoggerMessage.Define(
        LogLevel.Debug,
        new EventId(2001, nameof(LogSettingsChanged)),
        "Settings changed — invalidating WindowFilter exclusion cache."
    );

    private readonly ISettingsService _settings;
    private readonly ILogger<WindowFilter> _log;
    private readonly int _currentProcessId;
    private readonly object _cacheLock = new();

    // Swapped atomically by UpdateCache under _cacheLock. Reads on any thread
    // get a consistent snapshot (ImmutableHashSet is itself thread-safe).
    private volatile ImmutableHashSet<string> _excludedApps;

    private bool _disposed;

    /// <summary>
    /// Creates a new filter bound to the given settings service. The ctor takes
    /// the initial exclusion snapshot and registers a handler on
    /// <see cref="ISettingsService.Changed"/>.
    /// </summary>
    public WindowFilter(ISettingsService settings, ILogger<WindowFilter> log)
        : this(settings, log, Environment.ProcessId) { }

    /// <summary>
    /// Test-only seam: lets tests inject a fixed current-process id so
    /// rule 0 (self-exclusion) is deterministic without spawning processes.
    /// </summary>
    internal WindowFilter(
        ISettingsService settings,
        ILogger<WindowFilter> log,
        int currentProcessId
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);

        _settings = settings;
        _log = log;
        _currentProcessId = currentProcessId;
        _excludedApps = BuildExcludedSet(settings.Current);
        _settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc />
    public bool IsManageable(WindowSnapshot snapshot)
    {
        // Rule 0 — never manage Stagehand's own windows. Class-based exclusion
        // (rule 5) covers the production overlay + settings windows because we
        // register them with branded class names, but the harness's WPF main
        // window uses a default `HwndWrapper[…]` class — without this rule
        // "Enable Stage" would park the harness off-screen and look like a
        // crash. Cheap PID compare runs first so the rest of the pipeline
        // never sees our own HWNDs.
        if (snapshot.ProcessId == _currentProcessId)
        {
            return false;
        }

        // Rule 1 — IsWindowVisible.
        if (!snapshot.IsVisible)
        {
            return false;
        }

        // Rule 2 — Not cloaked (DWMWA_CLOAKED == 0).
        if (snapshot.IsCloaked)
        {
            return false;
        }

        // Rule 3 — GetAncestor(hwnd, GA_ROOT) == hwnd.
        if (!snapshot.IsTopLevel)
        {
            return false;
        }

        // Rule 4 — Non-empty title OR (WS_EX_APPWINDOW set, or (WS_EX_TOOLWINDOW
        // not set AND no owner)). Implements the standard "visible to Alt-Tab /
        // taskbar" heuristic from MSDN.
        if (!HasAppWindowShape(snapshot))
        {
            return false;
        }

        // Rule 5 — Class-name exclusion list (system shell + our own windows +
        // wallpaper-family prefixes).
        if (WindowClassExclusions.IsExcluded(snapshot.ClassName))
        {
            return false;
        }

        // Rule 6 — User-configured process or class-name exclusion.
        ImmutableHashSet<string> excluded = _excludedApps;
        if (excluded.Count > 0)
        {
            if (
                !string.IsNullOrEmpty(snapshot.ProcessName)
                && excluded.Contains(snapshot.ProcessName)
            )
            {
                return false;
            }

            if (!string.IsNullOrEmpty(snapshot.ClassName) && excluded.Contains(snapshot.ClassName))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Unsubscribes from <see cref="ISettingsService.Changed"/> so a long-lived
    /// settings service does not pin the filter instance in memory after the
    /// composition root releases it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _settings.Changed -= OnSettingsChanged;
        _disposed = true;
    }

    private static bool HasAppWindowShape(WindowSnapshot snapshot)
    {
        // WS_EX_APPWINDOW forces inclusion (e.g. tool-window titled apps that
        // still want to appear in Alt-Tab).
        const long WsExAppWindow = 0x00040000L;
        const long WsExToolWindow = 0x00000080L;

        if (!string.IsNullOrWhiteSpace(snapshot.Title))
        {
            return true;
        }

        if ((snapshot.ExStyle & WsExAppWindow) != 0)
        {
            return true;
        }

        var isToolWindow = (snapshot.ExStyle & WsExToolWindow) != 0;
        if (!isToolWindow && !snapshot.HasOwner)
        {
            return true;
        }

        return false;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        LogSettingsChanged(_log, null);
        lock (_cacheLock)
        {
            _excludedApps = BuildExcludedSet(settings);
        }
    }

    private static ImmutableHashSet<string> BuildExcludedSet(AppSettings settings)
    {
        ImmutableHashSet<string> source = settings.Behavior.ExcludedAppsOrEmpty;

        // Re-key the set under OrdinalIgnoreCase regardless of what the caller
        // passed in. Persisted settings always arrive via the record ctor so in
        // practice the comparer is already correct, but defensive re-keying
        // means a future hand-constructed AppSettings in a test cannot silently
        // make Rule 6 case-sensitive.
        if (ReferenceEquals(source.KeyComparer, StringComparer.OrdinalIgnoreCase))
        {
            return source;
        }

        return source.WithComparer(StringComparer.OrdinalIgnoreCase);
    }
}
