namespace App.Core.Stage;

/// <summary>
/// A window that has been (or is intended to be) parked by Stage, with the
/// information needed to restore it on disable.
/// </summary>
/// <remarks>
/// The monitor is identified by <see cref="OriginalMonitorDeviceName"/> (e.g.
/// <c>\\.\DISPLAY1</c>) rather than a volatile <c>HMONITOR</c>, because
/// HMONITOR values change across display-topology changes (monitor
/// connect/disconnect, resolution/orientation changes).
/// </remarks>
/// <param name="Identity">Stable identity of the parked window.</param>
/// <param name="OriginalBounds">Pre-park bounds in physical pixels.</param>
/// <param name="OriginalMonitorDeviceName">GDI device name of the monitor the window was on.</param>
/// <param name="IsElevated">
/// <see langword="true"/> when the window could not be parked because it
/// runs at a higher integrity level than Stagehand (e.g. an elevated
/// process or Task Manager). The scene still includes the entry — the
/// overlay surfaces it with a shield indicator (Plan 05) — but no Win32
/// move/resize call is attempted against the HWND while parked.
/// </param>
public sealed record ParkedWindow(
    WindowIdentity Identity,
    App.Interop.Rect OriginalBounds,
    string OriginalMonitorDeviceName,
    bool IsElevated
);
