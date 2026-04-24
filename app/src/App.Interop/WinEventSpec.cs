namespace App.Interop;

/// <summary>
/// Declarative description of a WinEvent subscription. Passed to
/// <see cref="IWinEventHookFactory.Create"/> to install a hook.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="EventMin"/> and <paramref name="EventMax"/> form the
/// inclusive range the OS delivers to the callback. To hook a single event
/// set both to the same id.
/// </para>
/// <para>
/// <paramref name="IdProcess"/> and <paramref name="IdThread"/> scope the
/// hook. Zero means "all processes / all threads" — convenient but costly
/// for high-frequency event classes such as
/// <c>EVENT_OBJECT_LOCATIONCHANGE</c>, which fires on every pixel of a
/// window drag across every window in the session. Plan 03's
/// <c>StageController</c> installs narrow per-HWND hooks by passing the
/// owning process id here.
/// </para>
/// <para>
/// <paramref name="CoalesceWindow"/> enables the hook wrapper's in-memory
/// dedupe ring buffer. When set, bursts of the same
/// <c>(EventId, Hwnd)</c> pair within the window are dropped. The buffer
/// is per <see cref="WinEventHook"/> instance — two independently-installed
/// hooks never share dedupe state.
/// </para>
/// </remarks>
/// <param name="EventMin">Lowest WinEvent id to observe (inclusive).</param>
/// <param name="EventMax">Highest WinEvent id to observe (inclusive).</param>
/// <param name="Flags">
/// <c>WINEVENT_*</c> flag bitmask. Defaults to
/// <see cref="WinEventFlags.OutOfContext"/>, the only safe mode for an
/// out-of-process subscriber.
/// </param>
/// <param name="IdProcess">Target process id, or <c>0</c> for all processes.</param>
/// <param name="IdThread">Target thread id, or <c>0</c> for all threads of the target process.</param>
/// <param name="CoalesceWindow">
/// Dedupe window. <see langword="null"/> disables coalescing.
/// </param>
public sealed record WinEventSpec(
    uint EventMin,
    uint EventMax,
    WinEventFlags Flags = WinEventFlags.OutOfContext,
    uint IdProcess = 0,
    uint IdThread = 0,
    TimeSpan? CoalesceWindow = null
);

/// <summary>
/// One delivered WinEvent notification, marshalled from the hook thread to
/// the UI thread before subscriber invocation.
/// </summary>
/// <param name="EventId">The WinEvent id that fired (one of the <c>EVENT_*</c> constants).</param>
/// <param name="Hwnd">The HWND the event is about.</param>
/// <param name="ObjectId">The <c>OBJID_*</c> indicating which accessibility object changed.</param>
/// <param name="ChildId">Child id within the object (<c>CHILDID_SELF = 0</c> for the object itself).</param>
/// <param name="Thread">Thread id that generated the event.</param>
/// <param name="Time">Event time in milliseconds (Win32 tick-count domain).</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Plan 02 §Design.5 specifies this exact type name. The 'Args' suffix describes the payload of the WinEventHook.Fired event and mirrors the BCL's EventArgs naming convention even though this type is a record (not derived from EventArgs)."
)]
public sealed record WinEventArgs(
    uint EventId,
    IntPtr Hwnd,
    int ObjectId,
    int ChildId,
    uint Thread,
    uint Time
);
