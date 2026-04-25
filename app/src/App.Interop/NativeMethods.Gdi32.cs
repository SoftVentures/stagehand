using System.Runtime.InteropServices;

namespace App.Interop;

internal static partial class NativeMethods
{
    // ---------------------------------------------------------------------
    // GDI constants used by the bitmap-capture path
    // ---------------------------------------------------------------------

    // BITMAPINFOHEADER.biCompression
    // <see href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader"/>
    internal const uint BI_RGB = 0;

    // GetDIBits / SetDIBits iUsage parameter — DIB_RGB_COLORS asks for
    // raw colour values (the alternative DIB_PAL_COLORS targets palettised
    // 8 bpp surfaces we never produce).
    // <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-getdibits"/>
    internal const uint DIB_RGB_COLORS = 0;

    // ---------------------------------------------------------------------
    // GDI structs
    // ---------------------------------------------------------------------

    /// <summary>
    /// Bitmap information header. The capture path stamps a 32 bpp BGRA layout
    /// (<c>biBitCount = 32</c>, <c>biCompression = BI_RGB</c>, top-down rows
    /// via negative <c>biHeight</c>).
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>
    /// Native <c>BITMAPINFO</c>. The <c>bmiColors</c> array is unused for
    /// 32 bpp BI_RGB surfaces but the struct's tail must still be present in
    /// the marshalled layout, so we model it as a one-entry colour table
    /// (matches the GDI documentation).
    /// </summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapinfo"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public RGBQUAD bmiColors;
    }

    /// <summary>RGBA colour-table entry. Unused for 32 bpp BI_RGB but present
    /// in the marshalled layout.</summary>
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-rgbquad"/>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }

    // ---------------------------------------------------------------------
    // Device contexts and bitmaps
    // ---------------------------------------------------------------------

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-createcompatibledc"/>
    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-deletedc"/>
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(IntPtr hdc);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-createcompatiblebitmap"/>
    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-selectobject"/>
    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial IntPtr SelectObject(IntPtr hdc, IntPtr hObj);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-deleteobject"/>
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr hObj);

    // consumer: plan 02 §BitmapThumbnailSource (capture path)
    // GetDIBits writes pixel data into the supplied byte buffer — using
    // [LibraryImport] with `byte[]` would force an extra marshal copy; the
    // [DllImport] form lets us pin once via `Span<byte>.GetPinnableReference`.
    /// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-getdibits"/>
    [DllImport("gdi32.dll", SetLastError = true, EntryPoint = "GetDIBits")]
    internal static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        [Out] byte[] lpvBits,
        ref BITMAPINFO lpbmi,
        uint uUsage
    );
}
