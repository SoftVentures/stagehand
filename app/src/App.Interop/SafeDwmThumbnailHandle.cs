using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace App.Interop;

/// <summary>
/// <see cref="SafeHandle"/> wrapper for DWM thumbnail handles returned by
/// <c>DwmRegisterThumbnail</c>. Ensures the handle is released via
/// <c>DwmUnregisterThumbnail</c> even across unexpected shutdowns.
/// </summary>
public sealed class SafeDwmThumbnailHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Creates an invalid handle owned by the runtime. Used by P/Invoke marshalling
    /// when <c>[Out]</c> returns a freshly-allocated thumbnail handle.
    /// </summary>
    public SafeDwmThumbnailHandle()
        : base(ownsHandle: true) { }

    /// <summary>
    /// Wraps an existing raw thumbnail handle.
    /// </summary>
    /// <param name="existingHandle">Raw HTHUMBNAIL from DWM.</param>
    /// <param name="ownsHandle">
    /// <c>true</c> to release via <c>DwmUnregisterThumbnail</c> when disposed.
    /// </param>
    public SafeDwmThumbnailHandle(IntPtr existingHandle, bool ownsHandle)
        : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        // SafeHandle must not take a DI dependency — its finalizer runs on a
        // background thread and may fire after the DI container is gone. Call
        // the P/Invoke directly. `DwmUnregisterThumbnail` is declared with
        // `PreserveSig = false`, so a non-zero HRESULT surfaces as COMException;
        // swallow it and report failure via the `false` return. The CLR logs
        // the SafeHandle release failure through SafeHandle diagnostics.
        try
        {
            NativeMethods.DwmUnregisterThumbnail(handle);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
