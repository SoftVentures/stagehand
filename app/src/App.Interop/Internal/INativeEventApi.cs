using App.Interop.Errors;

namespace App.Interop.Internal;

/// <summary>
/// Thin, testable seam over the WinEvent subscription APIs consumed by
/// <see cref="WinEventHook"/> and <see cref="WinEventHookThread"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the <see cref="INativeWindowApi"/> / <see cref="INativeDwmApi"/>
/// pattern: every method here maps to one native call and surfaces failures
/// through <see cref="Win32InteropException"/>. The seam is <c>internal</c>
/// because the raw Win32 surface is not part of Stagehand's public API; the
/// Tests assembly gets access via <c>InternalsVisibleTo</c>.
/// </para>
/// <para>
/// Tests substitute an NSubstitute fake for this interface so the hook
/// layer can be verified (callback marshalling, coalescing, dispose
/// resilience) without a real Win32 hook ever being installed.
/// </para>
/// </remarks>
internal interface INativeEventApi
{
    /// <summary>
    /// Installs an out-of-context WinEvent hook for the given range and
    /// process/thread scope, delivering callbacks via
    /// <paramref name="callback"/>. The returned
    /// <see cref="SafeWinEventHookHandle"/> owns the lifetime and unhooks
    /// on dispose.
    /// </summary>
    /// <param name="eventMin">Low bound of the event range (inclusive).</param>
    /// <param name="eventMax">High bound of the event range (inclusive).</param>
    /// <param name="callback">
    /// Delegate invoked by the OS on the hook thread. The caller is
    /// responsible for keeping the delegate alive for as long as the hook
    /// exists — typically by storing it on <see cref="WinEventHook"/>.
    /// </param>
    /// <param name="idProcess">Target process id, or <c>0</c> for all processes.</param>
    /// <param name="idThread">Target thread id, or <c>0</c> for all threads.</param>
    /// <param name="flags">
    /// <c>WINEVENT_*</c> flag bitmask. Callers pass
    /// <see cref="NativeMethods.WINEVENT_OUTOFCONTEXT"/>.
    /// </param>
    /// <exception cref="Win32InteropException">
    /// <c>SetWinEventHook</c> returned a null handle.
    /// </exception>
    SafeWinEventHookHandle SetWinEventHook(
        uint eventMin,
        uint eventMax,
        NativeMethods.WinEventDelegate callback,
        uint idProcess,
        uint idThread,
        uint flags
    );
}
