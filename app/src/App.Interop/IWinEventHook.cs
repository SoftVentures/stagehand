namespace App.Interop;

/// <summary>
/// One observed WinEvent notification.
/// </summary>
/// <param name="Event">WinEvent id (e.g. <c>EVENT_SYSTEM_FOREGROUND</c>).</param>
/// <param name="Hwnd">Window the event is about.</param>
/// <param name="ObjectId">OBJID_* indicating which UI object changed.</param>
/// <param name="ChildId">Child id within the object (CHILDID_SELF = 0 for the object itself).</param>
/// <param name="Thread">Thread id that generated the event.</param>
/// <param name="TimeMs">Event time in milliseconds (Win32 tick-count domain).</param>
public readonly record struct WinEvent(
    uint Event,
    IntPtr Hwnd,
    int ObjectId,
    int ChildId,
    uint Thread,
    uint TimeMs
);

/// <summary>
/// Subscribes to out-of-context WinEvents (foreground change, minimise, cloak,
/// location change, etc.). The hook lifetime follows the instance lifetime —
/// <see cref="IDisposable.Dispose"/> unhooks and releases the delegate pin.
/// </summary>
public interface IWinEventHook : IDisposable
{
    /// <summary>
    /// Raised on the hook's owning thread (the one that called <see cref="Start"/>).
    /// Handlers must not block the message pump.
    /// </summary>
    /// <remarks>
    /// CA1003 suggests an <see cref="EventArgs"/>-derived payload. <see cref="WinEvent"/>
    /// is a readonly record struct chosen deliberately for zero-allocation dispatch on
    /// the WinEvent callback hot path — allocating an <see cref="EventArgs"/> subclass
    /// per event (thousands per second under heavy activity) would add GC pressure to
    /// the shell's responsiveness-critical path. We therefore suppress the rule.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "WinEvent is a value-type payload for zero-alloc hot path dispatch."
    )]
    event EventHandler<WinEvent>? EventRaised;

    /// <summary>
    /// Installs the hook. Idempotent — calling twice has no effect.
    /// Must be invoked from a thread with an active message pump because
    /// <c>WINEVENT_OUTOFCONTEXT</c> callbacks are delivered via window messages.
    /// </summary>
    void Start();
}
