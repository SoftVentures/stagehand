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
        // TODO: Plan 02 — call NativeMethods.DwmUnregisterThumbnail(handle) and return result.
        return false;
    }
}
