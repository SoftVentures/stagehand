namespace App.Interop;

/// <summary>
/// Immutable snapshot of a top-level window's state at a point in time.
/// </summary>
/// <remarks>
/// The stable identity of a window is the pair
/// (<see cref="ProcessId"/>, <see cref="ProcessStartTimeUtcTicks"/>) — NOT the raw
/// <see cref="Hwnd"/>. Windows recycles HWND values aggressively, so any subsystem
/// that caches/compares windows across time must use the process identity pair to
/// guard against HWND reuse after a target process exits.
/// </remarks>
/// <param name="Hwnd">Raw window handle. Valid only for the lifetime of this snapshot.</param>
/// <param name="Title">Window title text at the time of the snapshot.</param>
/// <param name="ClassName">Win32 window class name.</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ProcessStartTimeUtcTicks">
/// Owning process start time in UTC ticks. Combined with <paramref name="ProcessId"/>
/// to form a stable identity that survives HWND reuse.
/// </param>
/// <param name="Bounds">Window bounds in physical pixels.</param>
/// <param name="MonitorHandle">HMONITOR the window is currently associated with.</param>
public readonly record struct WindowSnapshot(
    IntPtr Hwnd,
    string Title,
    string ClassName,
    int ProcessId,
    long ProcessStartTimeUtcTicks,
    Rect Bounds,
    IntPtr MonitorHandle
);
