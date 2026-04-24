using App.Interop;

namespace App.Interop.Internal;

/// <summary>
/// Thin, testable seam over the DWM thumbnail APIs consumed by
/// <see cref="DwmThumbnail"/> and <see cref="DwmThumbnailFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every method on this interface maps one-for-one onto a native DWM call in
/// <see cref="NativeMethods"/> and translates failures into
/// <see cref="App.Interop.Errors.Win32InteropException"/>. DWM APIs return
/// HRESULTs (not <c>GetLastError</c>); the implementation uses
/// <c>PreserveSig = false</c> so the marshaller surfaces failures as
/// <see cref="System.Runtime.InteropServices.COMException"/>, which the
/// seam catches and re-throws as
/// <see cref="App.Interop.Errors.Win32InteropException"/> carrying the HRESULT.
/// </para>
/// <para>
/// The seam is <c>internal</c> on purpose — the native surface is not part
/// of Stagehand's public API. The Tests assembly gets access via
/// <c>InternalsVisibleTo</c> (see <c>AssemblyInfo.cs</c>) so
/// <see cref="DwmThumbnailFactory"/> can be covered with an NSubstitute fake
/// without booting real DWM.
/// </para>
/// </remarks>
internal interface INativeDwmApi
{
    /// <summary>
    /// Registers a new DWM thumbnail that reflects <paramref name="source"/>
    /// onto <paramref name="destination"/>. The returned
    /// <see cref="SafeDwmThumbnailHandle"/> owns the lifetime and releases
    /// via <c>DwmUnregisterThumbnail</c> on dispose.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>DwmRegisterThumbnail</c> failed.
    /// </exception>
    SafeDwmThumbnailHandle DwmRegisterThumbnail(IntPtr destination, IntPtr source);

    /// <summary>
    /// Explicit unregister path. Normal callers rely on
    /// <see cref="SafeDwmThumbnailHandle"/> to release automatically; this
    /// exists so test fakes can observe both ends of the lifecycle and so
    /// future code paths can unregister without disposing the wrapper.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>DwmUnregisterThumbnail</c> failed.
    /// </exception>
    void DwmUnregisterThumbnail(IntPtr hThumbnail);

    /// <summary>
    /// Applies <paramref name="props"/> to the live thumbnail identified by
    /// <paramref name="hThumbnail"/>.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>DwmUpdateThumbnailProperties</c> failed.
    /// </exception>
    void DwmUpdateThumbnailProperties(
        IntPtr hThumbnail,
        in NativeMethods.DWM_THUMBNAIL_PROPERTIES props
    );

    /// <summary>
    /// Returns the source window's current size as reported by DWM.
    /// </summary>
    /// <exception cref="App.Interop.Errors.Win32InteropException">
    /// <c>DwmQueryThumbnailSourceSize</c> failed.
    /// </exception>
    Size DwmQueryThumbnailSourceSize(IntPtr hThumbnail);
}
