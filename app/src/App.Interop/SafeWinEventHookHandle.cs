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
    /// <remarks>
    /// Calls <c>UnhookWinEvent</c> on the wrapped HWINEVENTHOOK. Swallows any
    /// exception the P/Invoke might raise (e.g. during AppDomain/process
    /// teardown when user32 is partially unloaded) and reports the failure via
    /// the <see langword="false"/> return — the SafeHandle infrastructure then
    /// leaves the handle marked as released regardless. Process exit cleans up
    /// the underlying hook in either case.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "ReleaseHandle runs during finalisation; any thrown exception here crashes the GC."
    )]
    protected override bool ReleaseHandle()
    {
        try
        {
            return NativeMethods.UnhookWinEvent(handle);
        }
        catch
        {
            return false;
        }
    }
}
