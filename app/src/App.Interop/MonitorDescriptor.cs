namespace App.Interop;

/// <summary>
/// Snapshot of one monitor's identity and geometry at a point in time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity.</b> <see cref="DeviceName"/> (the GDI device name, e.g.
/// <c>\\.\DISPLAY1</c>) is the stable identifier across hot-plug events.
/// <see cref="Hmonitor"/> is the live HMONITOR — it is valid only for the
/// lifetime of this snapshot and may change after a display-topology
/// change. Persistent storage MUST use <see cref="DeviceName"/>.
/// </para>
/// <para>
/// <see cref="FullBounds"/> and <see cref="WorkArea"/> are in physical
/// pixels in the virtual-screen coordinate space (origin at the
/// primary monitor's top-left).
/// </para>
/// </remarks>
/// <param name="Hmonitor">Live HMONITOR — transient.</param>
/// <param name="DeviceName">Stable GDI device name across hot-plug.</param>
/// <param name="FullBounds">Full monitor rectangle in physical pixels.</param>
/// <param name="WorkArea">Work area (full minus taskbar) in physical pixels.</param>
/// <param name="IsPrimary">True when MONITORINFOF_PRIMARY was set.</param>
public readonly record struct MonitorDescriptor(
    IntPtr Hmonitor,
    string DeviceName,
    Rect FullBounds,
    Rect WorkArea,
    bool IsPrimary
);
