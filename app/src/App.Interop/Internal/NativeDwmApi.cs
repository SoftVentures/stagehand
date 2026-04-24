using System.Runtime.InteropServices;
using App.Interop.Errors;

namespace App.Interop.Internal;

/// <summary>
/// Production <see cref="INativeDwmApi"/> implementation. Translates
/// <see cref="COMException"/>s raised by the <c>PreserveSig = false</c>
/// DWM P/Invokes into <see cref="Win32InteropException"/>s preserving the
/// original HRESULT.
/// </summary>
internal sealed class NativeDwmApi : INativeDwmApi
{
    /// <inheritdoc />
    public SafeDwmThumbnailHandle DwmRegisterThumbnail(IntPtr destination, IntPtr source)
    {
        try
        {
            NativeMethods.DwmRegisterThumbnail(destination, source, out var raw);
            return new SafeDwmThumbnailHandle(raw, ownsHandle: true);
        }
        catch (COMException ex)
        {
            throw new Win32InteropException(
                ex.HResult,
                $"DwmRegisterThumbnail failed (HRESULT 0x{ex.HResult:X8}).",
                ex
            );
        }
    }

    /// <inheritdoc />
    public void DwmUnregisterThumbnail(IntPtr hThumbnail)
    {
        try
        {
            NativeMethods.DwmUnregisterThumbnail(hThumbnail);
        }
        catch (COMException ex)
        {
            throw new Win32InteropException(
                ex.HResult,
                $"DwmUnregisterThumbnail failed (HRESULT 0x{ex.HResult:X8}).",
                ex
            );
        }
    }

    /// <inheritdoc />
    public void DwmUpdateThumbnailProperties(
        IntPtr hThumbnail,
        in NativeMethods.DWM_THUMBNAIL_PROPERTIES props
    )
    {
        // Defensive local copy: `ref` parameter of the P/Invoke requires a
        // writable location; the `in` on our seam reflects caller intent.
        NativeMethods.DWM_THUMBNAIL_PROPERTIES propsCopy = props;
        try
        {
            NativeMethods.DwmUpdateThumbnailProperties(hThumbnail, ref propsCopy);
        }
        catch (COMException ex)
        {
            throw new Win32InteropException(
                ex.HResult,
                $"DwmUpdateThumbnailProperties failed (HRESULT 0x{ex.HResult:X8}).",
                ex
            );
        }
    }

    /// <inheritdoc />
    public Size DwmQueryThumbnailSourceSize(IntPtr hThumbnail)
    {
        try
        {
            NativeMethods.DwmQueryThumbnailSourceSize(hThumbnail, out NativeMethods.SIZE size);
            return new Size(size.cx, size.cy);
        }
        catch (COMException ex)
        {
            throw new Win32InteropException(
                ex.HResult,
                $"DwmQueryThumbnailSourceSize failed (HRESULT 0x{ex.HResult:X8}).",
                ex
            );
        }
    }
}
