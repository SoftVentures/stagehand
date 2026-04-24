namespace App.Core.Stage;

/// <summary>
/// Stable identity tuple for a top-level window, decoupled from raw HWND reuse.
/// </summary>
/// <remarks>
/// Windows recycles HWND values aggressively once a process exits. Identity in
/// Stagehand is the pair (<see cref="ProcessId"/>, <see cref="ProcessStartTimeUtcTicks"/>),
/// with the current <see cref="Hwnd"/> carried alongside for convenience. Never
/// rely on HWND alone to compare windows across time.
/// </remarks>
/// <param name="Hwnd">Current window handle (may become invalid).</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ProcessStartTimeUtcTicks">Owning process start time in UTC ticks.</param>
public readonly record struct WindowIdentity(
    IntPtr Hwnd,
    int ProcessId,
    long ProcessStartTimeUtcTicks
);
