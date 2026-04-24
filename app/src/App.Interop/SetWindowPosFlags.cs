using System;

namespace App.Interop;

/// <summary>
/// Managed wrapper for the Win32 <c>SWP_*</c> flags passed to
/// <c>SetWindowPos</c>. Mirrors the constants in
/// <see cref="NativeMethods"/> one-to-one so callers can pass a strongly-typed
/// value instead of a raw <see cref="uint"/> bitmask.
/// </summary>
/// <remarks>
/// Plan 02 §S1 consumes this enum from <c>NativeWindowApi.SetWindowPos</c>.
/// Values are kept in sync with <c>NativeMethods.User32.cs</c> (<c>SWP_*</c>).
/// </remarks>
/// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos"/>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "Win32 SetWindowPos takes a UINT bitmask; the underlying type must match the P/Invoke signature exactly to avoid re-marshalling at every call site."
)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Name matches the Win32 SWP_* flag group and the pattern already used elsewhere for [Flags] enums (e.g. WindowStyles would also trip). The 'Flags' suffix communicates intent at the call site."
)]
public enum SetWindowPosFlags : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>Retains the current size (ignores the cx and cy parameters). <c>SWP_NOSIZE</c>.</summary>
    NoSize = 0x0001,

    /// <summary>Retains the current position (ignores X and Y). <c>SWP_NOMOVE</c>.</summary>
    NoMove = 0x0002,

    /// <summary>Retains the current Z order. <c>SWP_NOZORDER</c>.</summary>
    NoZOrder = 0x0004,

    /// <summary>Does not redraw changes. <c>SWP_NOREDRAW</c>.</summary>
    NoRedraw = 0x0008,

    /// <summary>Does not activate the window. <c>SWP_NOACTIVATE</c>.</summary>
    NoActivate = 0x0010,

    /// <summary>Applies new frame styles set via <c>SetWindowLong</c>. <c>SWP_FRAMECHANGED</c>.</summary>
    FrameChanged = 0x0020,

    /// <summary>Displays the window. <c>SWP_SHOWWINDOW</c>.</summary>
    ShowWindow = 0x0040,

    /// <summary>Hides the window. <c>SWP_HIDEWINDOW</c>.</summary>
    HideWindow = 0x0080,

    /// <summary>Discards the entire client-area contents. <c>SWP_NOCOPYBITS</c>.</summary>
    NoCopyBits = 0x0100,

    /// <summary>Does not change the owner window's position in the Z order. <c>SWP_NOOWNERZORDER</c>.</summary>
    NoOwnerZOrder = 0x0200,

    /// <summary>Prevents the window from receiving <c>WM_WINDOWPOSCHANGING</c>. <c>SWP_NOSENDCHANGING</c>.</summary>
    NoSendChanging = 0x0400,
}
