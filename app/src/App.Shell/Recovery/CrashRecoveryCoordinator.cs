using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Interop.Errors;
using App.Services.Snapshot;
using Microsoft.Extensions.Logging;

namespace App.Shell.Recovery;

/// <summary>
/// On app startup, checks for a stale snapshot from a previous Stagehand
/// process that crashed while enabled, and restores window positions /
/// work areas. Plan 03 §Design.9 step 3.
/// </summary>
/// <remarks>
/// <para>
/// Plan 03 ships an automatic-restore path: when a stale snapshot is
/// detected, every still-valid window identity is restored without user
/// confirmation, then the snapshot is deleted. Plan 04 wraps this in a
/// tray-balloon prompt with explicit Restore/Discard buttons; Plan 03
/// trades the prompt for simplicity and correctness.
/// </para>
/// </remarks>
public sealed class CrashRecoveryCoordinator
{
    private static readonly Action<ILogger, int, int, Exception?> s_logRecovering =
        LoggerMessage.Define<int, int>(
            LogLevel.Warning,
            new EventId(14001, nameof(CrashRecoveryCoordinator) + ".Recovering"),
            "Crash recovery: prior Stagehand left {SceneCount} stages with {WindowCount} windows; auto-restoring."
        );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logRestoreFailed =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(14002, nameof(CrashRecoveryCoordinator) + ".RestoreFailed"),
            "Crash recovery: RestorePosition for HWND 0x{Hwnd:X} failed; window may have already moved or closed."
        );

    private static readonly Action<ILogger, Exception?> s_logComplete = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(14003, nameof(CrashRecoveryCoordinator) + ".Complete"),
        "Crash recovery complete; snapshot deleted."
    );

    private readonly ISnapshotStore _store;
    private readonly ISnapshotReader _reader;
    private readonly IWindowController _windows;
    private readonly IWorkAreaManager _workArea;
    private readonly MonitorEnumerator _monitors;
    private readonly ILogger<CrashRecoveryCoordinator> _log;

    /// <summary>Production constructor.</summary>
    public CrashRecoveryCoordinator(
        ISnapshotStore store,
        ISnapshotReader reader,
        IWindowController windows,
        IWorkAreaManager workArea,
        MonitorEnumerator monitors,
        ILogger<CrashRecoveryCoordinator> log
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _workArea = workArea ?? throw new ArgumentNullException(nameof(workArea));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Detects a stale snapshot and restores from it. No-op when no
    /// snapshot exists or the prior process is still alive.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Recovery is best-effort; one bad window restore must not abort the rest."
    )]
    public async Task RunAsync(CancellationToken ct)
    {
        SnapshotFile? file = _reader.ReadIfStale();
        if (file is null)
        {
            return;
        }

        var totalWindows = 0;
        foreach (SnapshotScene s in file.Scenes)
        {
            totalWindows += s.Windows.Count;
        }
        s_logRecovering(_log, file.Scenes.Count, totalWindows, null);

        foreach (SnapshotScene scene in file.Scenes)
        {
            foreach (SnapshotWindow w in scene.Windows)
            {
                if (w.IsElevated)
                {
                    continue;
                }
                try
                {
                    var rect = new App.Interop.Rect(
                        w.OriginalBounds.X,
                        w.OriginalBounds.Y,
                        w.OriginalBounds.W,
                        w.OriginalBounds.H
                    );
                    _windows.RestorePosition(new IntPtr(w.Identity.Hwnd), rect);
                }
                catch (Win32InteropException)
                {
                    s_logRestoreFailed(_log, new IntPtr(w.Identity.Hwnd), null);
                }
                catch (ElevationBoundaryException)
                {
                    s_logRestoreFailed(_log, new IntPtr(w.Identity.Hwnd), null);
                }
            }
        }

        // Restore work areas — re-resolve hmonitor by device name (it may
        // have changed across the crash boundary).
        IReadOnlyList<MonitorDescriptor> currentMons = _monitors.EnumerateAll();
        foreach (SnapshotMonitor m in file.Monitors)
        {
            foreach (MonitorDescriptor cm in currentMons)
            {
                if (string.Equals(cm.DeviceName, m.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    var rect = new App.Interop.Rect(
                        m.SavedWorkArea.X,
                        m.SavedWorkArea.Y,
                        m.SavedWorkArea.W,
                        m.SavedWorkArea.H
                    );
                    _workArea.SetWorkArea(cm.Hmonitor, rect);
                    break;
                }
            }
        }

        await _store.DeleteAsync(ct).ConfigureAwait(false);
        s_logComplete(_log, null);
    }
}
