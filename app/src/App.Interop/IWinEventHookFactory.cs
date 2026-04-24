namespace App.Interop;

/// <summary>
/// Builds <see cref="WinEventHook"/> instances for a given
/// <see cref="WinEventSpec"/>. Callers own the returned hook and must
/// dispose it when no longer needed.
/// </summary>
/// <remarks>
/// <para>
/// The factory centralises the shared dependencies (UI dispatcher, hook
/// thread, native-event seam, logger factory) so consumers in
/// <c>App.Services</c> / <c>App.Shell</c> can request hooks by spec alone
/// without having to thread low-level Win32 types through their own
/// constructors.
/// </para>
/// <para>
/// Returned hooks share the same <see cref="Threading.WinEventHookThread"/>
/// — installing a thousand narrow hooks does not spawn a thousand threads.
/// Per-hook isolation (coalescing rings, <see cref="WinEventHook.Fired"/>
/// subscribers) is preserved because each hook owns its own state.
/// </para>
/// </remarks>
public interface IWinEventHookFactory
{
    /// <summary>
    /// Installs a new WinEvent hook matching <paramref name="spec"/> and
    /// returns the wrapper. Dispose the wrapper to unhook.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>SetWinEventHook</c> returned a null handle.
    /// </exception>
    WinEventHook Create(WinEventSpec spec);
}
