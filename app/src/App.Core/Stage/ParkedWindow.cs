namespace App.Core.Stage;

/// <summary>
/// A window that has been parked by the Stage, along with the information needed
/// to restore it when Stage is disabled.
/// </summary>
/// <remarks>
/// The monitor is identified by <see cref="MonitorDeviceName"/> (e.g.
/// <c>\\.\DISPLAY1</c>) rather than a volatile <c>HMONITOR</c>, because HMONITOR
/// values change across display topology changes (monitor connect/disconnect,
/// resolution/orientation changes).
/// </remarks>
/// <param name="Identity">Stable identity of the parked window.</param>
/// <param name="OriginalBounds">Pre-park bounds in physical pixels.</param>
/// <param name="MonitorDeviceName">GDI device name of the monitor the window was on.</param>
public readonly record struct ParkedWindow(
    WindowIdentity Identity,
    App.Interop.Rect OriginalBounds,
    string MonitorDeviceName
);
