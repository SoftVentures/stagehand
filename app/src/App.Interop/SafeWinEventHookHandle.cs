using Microsoft.Win32.SafeHandles;

namespace App.Interop;

/// <summary>
/// <see cref="SafeHandle"/> wrapper for a WinEvent hook handle returned by
/// <c>SetWinEventHook</c>. Released with <c>UnhookWinEvent</c>.
/// </summary>
public sealed class SafeWinEventHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Creates an invalid handle owned by the runtime.
    /// </summary>
    public SafeWinEventHookHandle()
        : base(ownsHandle: true) { }

    /// <summary>
    /// Wraps an existing raw HWINEVENTHOOK.
    /// </summary>
    /// <param name="existingHandle">Raw HWINEVENTHOOK from <c>SetWinEventHook</c>.</param>
    /// <param name="ownsHandle">
    /// <c>true</c> to release via <c>UnhookWinEvent</c> when disposed.
    /// </param>
    public SafeWinEventHookHandle(IntPtr existingHandle, bool ownsHandle)
        : base(ownsHandle)
    {
        SetHandle(existingHandle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        // TODO: Plan 02 — call NativeMethods.UnhookWinEvent(handle) and return result.
        return false;
    }
}
