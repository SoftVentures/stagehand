using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using App.Interop.Errors;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Real <see cref="IWorkAreaManager"/> implementation. Wraps
/// <c>SystemParametersInfoW(SPI_SETWORKAREA, ...)</c> with
/// <c>SPIF_SENDCHANGE</c> so Explorer reflows immediately. Tracks the
/// pre-modification work area of every monitor it touches by GDI device
/// name, so <see cref="RestoreAll"/> survives intervening hot-plug events
/// (HMONITOR values change across hot-plug; device names do not).
/// </summary>
/// <remarks>
/// <para>
/// <b>Persistence flag.</b> The <c>SPIF_UPDATEINIFILE</c> flag is
/// deliberately NOT set — Stagehand must not persist its sidebar
/// reservation across reboots. <c>SPIF_SENDCHANGE</c> alone broadcasts
/// <c>WM_SETTINGCHANGE</c> so other apps that watch the work area pick
/// up the new rect immediately.
/// </para>
/// <para>
/// <b>Idempotency.</b> Calling <see cref="SetWorkArea"/> with the same
/// rect twice is a no-op at the OS layer; the first call captures the
/// "original" (pre-modification) rect and subsequent calls overwrite the
/// applied rect without losing the original.
/// </para>
/// </remarks>
public sealed class WorkAreaManager : IWorkAreaManager
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logUnknownHmon =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(1, nameof(WorkAreaManager) + ".UnknownHmon"),
            "WorkAreaManager: HMONITOR 0x{HMonitor:X} not found in current monitor enumeration; SetWorkArea ignored."
        );

    private static readonly Action<ILogger, string, int, Exception?> s_logSpiFailed =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(2, nameof(WorkAreaManager) + ".SpiFailed"),
            "WorkAreaManager: SPI_SETWORKAREA failed for monitor '{Device}' (Win32 error {Error})."
        );

    private static readonly Action<ILogger, string, Exception?> s_logRestoreSkipped =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, nameof(WorkAreaManager) + ".RestoreSkipped"),
            "WorkAreaManager.RestoreAll: monitor '{Device}' is no longer attached; original work area not restored."
        );

    private readonly Func<IReadOnlyList<MonitorDescriptor>> _enumerate;
    private readonly Func<Rect, bool> _applyWorkArea;
    private readonly ILogger<WorkAreaManager> _log;

    private readonly object _gate = new();
    private readonly Dictionary<string, Rect> _originalByDevice = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Production constructor.</summary>
    public WorkAreaManager(MonitorEnumerator monitors, ILogger<WorkAreaManager> log)
        : this(EnumerateFrom(monitors), ApplyWorkAreaWin32, log) { }

    private static Func<IReadOnlyList<MonitorDescriptor>> EnumerateFrom(MonitorEnumerator monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return monitors.EnumerateAll;
    }

    /// <summary>
    /// Test constructor — replaces both the enumeration and the SPI call
    /// with delegate seams. Visible to <c>App.Tests</c> via
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    internal WorkAreaManager(
        Func<IReadOnlyList<MonitorDescriptor>> enumerate,
        Func<Rect, bool> applyWorkArea,
        ILogger<WorkAreaManager> log
    )
    {
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _applyWorkArea = applyWorkArea ?? throw new ArgumentNullException(nameof(applyWorkArea));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Rect GetWorkArea(IntPtr monitorHandle)
    {
        IReadOnlyList<MonitorDescriptor> monitors = _enumerate();
        foreach (MonitorDescriptor m in monitors)
        {
            if (m.Hmonitor == monitorHandle)
            {
                return m.WorkArea;
            }
        }
        return Rect.Empty;
    }

    /// <inheritdoc />
    public void SetWorkArea(IntPtr monitorHandle, Rect area)
    {
        IReadOnlyList<MonitorDescriptor> monitors = _enumerate();
        MonitorDescriptor? found = null;
        foreach (MonitorDescriptor m in monitors)
        {
            if (m.Hmonitor == monitorHandle)
            {
                found = m;
                break;
            }
        }

        if (found is not { } descriptor)
        {
            s_logUnknownHmon(_log, monitorHandle, null);
            return;
        }

        lock (_gate)
        {
            if (!_originalByDevice.ContainsKey(descriptor.DeviceName))
            {
                _originalByDevice[descriptor.DeviceName] = descriptor.WorkArea;
            }
        }

        if (!_applyWorkArea(area))
        {
            var err = Marshal.GetLastWin32Error();
            s_logSpiFailed(_log, descriptor.DeviceName, err, null);
        }
    }

    /// <inheritdoc />
    public void RestoreAll()
    {
        Dictionary<string, Rect> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, Rect>(
                _originalByDevice,
                StringComparer.OrdinalIgnoreCase
            );
            _originalByDevice.Clear();
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        IReadOnlyList<MonitorDescriptor> currentMonitors = _enumerate();
        foreach (KeyValuePair<string, Rect> pair in snapshot)
        {
            var matched = false;
            foreach (MonitorDescriptor m in currentMonitors)
            {
                if (string.Equals(m.DeviceName, pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    if (!_applyWorkArea(pair.Value))
                    {
                        var err = Marshal.GetLastWin32Error();
                        s_logSpiFailed(_log, pair.Key, err, null);
                    }
                    break;
                }
            }
            if (!matched)
            {
                s_logRestoreSkipped(_log, pair.Key, null);
            }
        }
    }

    /// <summary>
    /// Snapshot of the rects currently being tracked for restore. For tests
    /// — production callers have no reason to read this.
    /// </summary>
    internal IReadOnlyDictionary<string, Rect> SavedOriginalsForTesting()
    {
        lock (_gate)
        {
            return new Dictionary<string, Rect>(
                _originalByDevice,
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "RECT is a value type with no managed resources; nothing to dispose."
    )]
    private static bool ApplyWorkAreaWin32(Rect rect)
    {
        var native = new NativeMethods.RECT
        {
            Left = rect.X,
            Top = rect.Y,
            Right = rect.X + rect.Width,
            Bottom = rect.Y + rect.Height,
        };
        return NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref native,
            NativeMethods.SPIF_SENDCHANGE
        );
    }
}
