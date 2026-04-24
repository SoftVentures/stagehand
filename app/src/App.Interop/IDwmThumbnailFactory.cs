namespace App.Interop;

/// <summary>
/// Creates <see cref="DwmThumbnail"/> instances that reflect a live preview
/// of a source HWND into a destination HWND (usually the sidebar overlay).
/// </summary>
/// <remarks>
/// The only production implementation is <see cref="DwmThumbnailFactory"/>.
/// Tests can substitute a fake that returns pre-seeded wrappers; the
/// production factory wraps the <see cref="Internal.INativeDwmApi"/> seam so
/// lifecycle tests can observe register / unregister pairs without booting
/// DWM.
/// </remarks>
public interface IDwmThumbnailFactory
{
    /// <summary>
    /// Registers a new thumbnail reflecting <paramref name="sourceHwnd"/>
    /// onto <paramref name="destinationHwnd"/>. The returned wrapper owns
    /// the DWM registration; disposing it tears the registration down.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>DwmRegisterThumbnail</c> failed. The caller may treat the source
    /// window as un-registrable and continue without it.
    /// </exception>
    DwmThumbnail Register(IntPtr sourceHwnd, IntPtr destinationHwnd);
}
