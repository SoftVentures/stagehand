using System.Runtime.InteropServices;
using App.Interop.Errors;

namespace App.Interop.Internal;

/// <summary>
/// Production implementation of <see cref="INativeEventApi"/>. Delegates to
/// <see cref="NativeMethods.SetWinEventHook"/> and wraps the returned raw
/// HWINEVENTHOOK in a <see cref="SafeWinEventHookHandle"/>.
/// </summary>
internal sealed class NativeEventApi : INativeEventApi
{
    public SafeWinEventHookHandle SetWinEventHook(
        uint eventMin,
        uint eventMax,
        NativeMethods.WinEventDelegate callback,
        uint idProcess,
        uint idThread,
        uint flags
    )
    {
        ArgumentNullException.ThrowIfNull(callback);

        var raw = NativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            IntPtr.Zero,
            callback,
            idProcess,
            idThread,
            flags
        );

        if (raw == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32InteropException(
                err,
                $"{nameof(NativeMethods.SetWinEventHook)} returned null (Win32 error {err})."
            );
        }

        return new SafeWinEventHookHandle(raw, ownsHandle: true);
    }
}
