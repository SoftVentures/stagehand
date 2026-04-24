using System.Diagnostics;
using App.Interop.Errors;
using App.Interop.Internal;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Real <see cref="IWindowEnumerator"/> implementation that walks the desktop's
/// top-level windows via <see cref="INativeWindowApi"/> and builds a fully-
/// populated <see cref="WindowSnapshot"/> per handle.
/// </summary>
/// <remarks>
/// <para>
/// The enumerator returns the raw snapshots — filter-rule enforcement lives in
/// <c>App.Services.Windows.WindowFilter</c>. The split is a consequence of the
/// layer direction: <c>App.Interop</c> cannot reference <c>App.Services</c>, so
/// combining enumerator + filter happens one layer up in
/// <c>ManageableWindowService</c>.
/// </para>
/// <para>
/// Per-window failures (target process exited mid-enumeration, DWM returns
/// <c>E_INVALIDARG</c> on a stale HWND, etc.) are swallowed: the offending
/// window is skipped, a <see cref="LogLevel.Debug"/> entry is emitted, and the
/// enumeration continues. A single flaky window must never poison a refresh
/// pass — the Stage UI recovers on the next tick.
/// </para>
/// </remarks>
public sealed class WindowEnumerator : IWindowEnumerator
{
    private static readonly Action<ILogger, IntPtr, string, string, Exception?> LogSnapshotFailed =
        LoggerMessage.Define<IntPtr, string, string>(
            LogLevel.Debug,
            new EventId(2101, nameof(LogSnapshotFailed)),
            "Skipping window {Hwnd} during enumeration ({ExceptionType}): {Reason}"
        );

    private readonly INativeWindowApi _api;
    private readonly Func<int, string?> _processNameLookup;
    private readonly ILogger<WindowEnumerator> _log;

    /// <summary>
    /// Production constructor. Constructs the default
    /// <see cref="NativeWindowApi"/> seam internally and resolves process
    /// names via <see cref="Process.GetProcessById(int)"/>. Called from the
    /// <c>ServiceConfiguration</c> composition root in <c>App.Shell</c>.
    /// </summary>
    public WindowEnumerator(ILogger<WindowEnumerator> log)
        : this(new NativeWindowApi(), DefaultProcessNameLookup, log) { }

    /// <summary>
    /// Test-facing constructor. Injects an explicit <see cref="INativeWindowApi"/>
    /// and a process-name lookup so tests do not need a real desktop or real
    /// processes. Marked <c>internal</c> to keep the production surface small;
    /// <c>App.Tests</c> consumes it via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal WindowEnumerator(
        INativeWindowApi api,
        Func<int, string?> processNameLookup,
        ILogger<WindowEnumerator> log
    )
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(processNameLookup);
        ArgumentNullException.ThrowIfNull(log);

        _api = api;
        _processNameLookup = processNameLookup;
        _log = log;
    }

    /// <inheritdoc />
    public IReadOnlyList<WindowSnapshot> GetManageableWindows()
    {
        IReadOnlyList<nint> handles = _api.EnumTopLevel();
        var snapshots = new List<WindowSnapshot>(handles.Count);
        foreach (var hwnd in handles)
        {
            WindowSnapshot? snap = TryBuildSnapshot(hwnd);
            if (snap is not null)
            {
                snapshots.Add(snap.Value);
            }
        }
        return snapshots;
    }

    private WindowSnapshot? TryBuildSnapshot(IntPtr hwnd)
    {
        try
        {
            var isTopLevel = _api.GetAncestorRoot(hwnd) == hwnd;
            var isVisible = _api.IsWindowVisible(hwnd);
            var isCloaked = _api.IsCloaked(hwnd);
            var title = _api.GetWindowText(hwnd);
            var className = _api.GetClassName(hwnd);
            var style = _api.GetWindowStyle(hwnd);
            var exStyle = _api.GetWindowExStyle(hwnd);
            var hasOwner = _api.GetWindowOwner(hwnd) != IntPtr.Zero;
            Rect bounds = _api.GetWindowRect(hwnd);
            var processId = _api.GetProcessId(hwnd);
            var processStart = _api.GetProcessStartTimeUtcTicks(processId);
            var monitor = _api.MonitorFromWindow(hwnd);
            var processName = _processNameLookup(processId) ?? string.Empty;

            return new WindowSnapshot(
                Hwnd: hwnd,
                Title: title,
                ClassName: className,
                ProcessId: processId,
                ProcessStartTimeUtcTicks: processStart,
                Bounds: bounds,
                Monitor: monitor,
                IsVisible: isVisible,
                IsCloaked: isCloaked,
                IsTopLevel: isTopLevel,
                Style: style,
                ExStyle: exStyle,
                HasOwner: hasOwner,
                ProcessName: processName
            );
        }
        catch (Win32InteropException ex)
        {
            LogSnapshotFailed(_log, hwnd, ex.GetType().Name, ex.Message, ex);
            return null;
        }
        catch (ArgumentException ex)
        {
            // Process.GetProcessById throws when the PID has vanished between
            // GetProcessId and the lookup call.
            LogSnapshotFailed(_log, hwnd, ex.GetType().Name, ex.Message, ex);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // Process.GetProcessById/ProcessName can throw when the process has
            // already exited.
            LogSnapshotFailed(_log, hwnd, ex.GetType().Name, ex.Message, ex);
            return null;
        }
    }

    private static string? DefaultProcessNameLookup(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.ProcessName;
        }
        catch (ArgumentException)
        {
            // Process no longer exists.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process has exited between GetProcessById and property access.
            return null;
        }
    }
}
