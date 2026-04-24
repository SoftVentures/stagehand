using System.Runtime.InteropServices;

namespace App.Interop;

internal static partial class NativeMethods
{
    // ---------------------------------------------------------------------
    // DWM thumbnail property flags (DWM_TNP_*)
    // ---------------------------------------------------------------------
    // <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/ns-dwmapi-dwm_thumbnail_properties"/>
    internal const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    internal const uint DWM_TNP_RECTSOURCE = 0x00000002;
    internal const uint DWM_TNP_OPACITY = 0x00000004;
    internal const uint DWM_TNP_VISIBLE = 0x00000008;
    internal const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    // ---------------------------------------------------------------------
    // DWM window attributes (subset relevant to Stagehand) — see
    // <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute"/>
    // ---------------------------------------------------------------------
    internal const uint DWMWA_NCRENDERING_ENABLED = 1;
    internal const uint DWMWA_NCRENDERING_POLICY = 2;
    internal const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    internal const uint DWMWA_ALLOW_NCPAINT = 4;
    internal const uint DWMWA_CAPTION_BUTTON_BOUNDS = 5;
    internal const uint DWMWA_NONCLIENT_RTL_LAYOUT = 6;
    internal const uint DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
    internal const uint DWMWA_FLIP3D_POLICY = 8;
    internal const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const uint DWMWA_HAS_ICONIC_BITMAP = 10;
    internal const uint DWMWA_DISALLOW_PEEK = 11;
    internal const uint DWMWA_EXCLUDED_FROM_PEEK = 12;
    internal const uint DWMWA_CLOAK = 13;
    internal const uint DWMWA_CLOAKED = 14;
    internal const uint DWMWA_FREEZE_REPRESENTATION = 15;
    internal const uint DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    internal const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const uint DWMWA_BORDER_COLOR = 34;
    internal const uint DWMWA_CAPTION_COLOR = 35;
    internal const uint DWMWA_TEXT_COLOR = 36;
    internal const uint DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37;
    internal const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // ---------------------------------------------------------------------
    // Structs
    // ---------------------------------------------------------------------

    /// <summary>
    /// Properties passed to <c>DwmUpdateThumbnailProperties</c>.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/ns-dwmapi-dwm_thumbnail_properties"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fVisible;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fSourceClientAreaOnly;
    }

    /// <summary>
    /// Native SIZE used by <c>DwmQueryThumbnailSourceSize</c>.
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/windef/ns-windef-size"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
    }

    // ---------------------------------------------------------------------
    // DWM thumbnails
    // ---------------------------------------------------------------------

    // consumer: plan 02 §DwmThumbnail
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail"/>
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

    // consumer: plan 02 §DwmThumbnail (SafeDwmThumbnailHandle.ReleaseHandle)
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmunregisterthumbnail"/>
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmUnregisterThumbnail(IntPtr thumb);

    // consumer: plan 02 §DwmThumbnail
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmupdatethumbnailproperties"/>
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmUpdateThumbnailProperties(
        IntPtr hThumbnailId,
        ref DWM_THUMBNAIL_PROPERTIES ptnProperties
    );

    // consumer: plan 02 §DwmThumbnail
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmquerythumbnailsourcesize"/>
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out SIZE pSize);

    // ---------------------------------------------------------------------
    // DWM window attributes
    // ---------------------------------------------------------------------

    // consumer: plan 02 §WindowEnumerator (DWMWA_CLOAKED) / §Theming (dark mode, backdrop)
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute"/>
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmGetWindowAttribute(
        IntPtr hwnd,
        uint dwAttribute,
        out int pvAttribute,
        uint cbAttribute
    );

    // consumer: plan 02 §Theming (DWMWA_USE_IMMERSIVE_DARK_MODE / DWMWA_SYSTEMBACKDROP_TYPE)
    /// <see href="https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute"/>
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmSetWindowAttribute(
        IntPtr hwnd,
        uint dwAttribute,
        in int pvAttribute,
        uint cbAttribute
    );
}
