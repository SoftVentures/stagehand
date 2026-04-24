namespace App.Core.Stage;

/// <summary>
/// A monitor's original work area, captured before Stage shrank it to make room
/// for the sidebar. Used to restore the work area on <c>DisableAsync</c>.
/// </summary>
/// <param name="MonitorDeviceName">GDI device name of the monitor (e.g. <c>\\.\DISPLAY1</c>).</param>
/// <param name="OriginalWorkArea">The monitor's work area in physical pixels prior to Stage.</param>
public readonly record struct SavedWorkArea(
    string MonitorDeviceName,
    App.Interop.Rect OriginalWorkArea
);
