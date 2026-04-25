using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace App.Interop.Internal;

/// <summary>
/// Production <see cref="INativeBitmapApi"/> implementation. Orchestrates the
/// full <c>PrintWindow</c> capture pipeline with strict GDI-handle hygiene:
/// every <c>CreateCompatibleDC</c> / <c>CreateCompatibleBitmap</c> / <c>GetDC</c>
/// is paired with a <c>finally</c>-released counterpart so the soak harness
/// from Plan 02 §S9 sees no GDI growth across thousands of captures.
/// </summary>
internal sealed class NativeBitmapApi : INativeBitmapApi
{
    private static readonly Action<ILogger, IntPtr, string, Exception?> s_logCaptureFailed =
        LoggerMessage.Define<IntPtr, string>(
            LogLevel.Debug,
            new EventId(1, nameof(NativeBitmapApi) + ".CaptureFailed"),
            "Bitmap capture for HWND 0x{Hwnd:X} aborted at {Stage}."
        );

    private readonly ILogger<NativeBitmapApi> _log;

    public NativeBitmapApi(ILogger<NativeBitmapApi> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public ThumbnailSnapshot? CaptureWindow(
        IntPtr hwnd,
        int width,
        int height,
        uint printWindowFlags
    )
    {
        if (width <= 0 || height <= 0)
        {
            // Caller is expected to pre-validate, but defend in depth.
            s_logCaptureFailed(_log, hwnd, "degenerate-size", null);
            return null;
        }

        IntPtr windowDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousObject = IntPtr.Zero;
        try
        {
            windowDc = NativeMethods.GetDC(hwnd);
            if (windowDc == IntPtr.Zero)
            {
                s_logCaptureFailed(_log, hwnd, "GetDC", null);
                return null;
            }

            memDc = NativeMethods.CreateCompatibleDC(windowDc);
            if (memDc == IntPtr.Zero)
            {
                s_logCaptureFailed(_log, hwnd, "CreateCompatibleDC", null);
                return null;
            }

            bitmap = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                s_logCaptureFailed(_log, hwnd, "CreateCompatibleBitmap", null);
                return null;
            }

            previousObject = NativeMethods.SelectObject(memDc, bitmap);

            if (!NativeMethods.PrintWindow(hwnd, memDc, printWindowFlags))
            {
                s_logCaptureFailed(_log, hwnd, "PrintWindow", null);
                return null;
            }

            // 32 bpp top-down BGRA: negative biHeight asks GDI to lay rows out
            // top-to-bottom (matches WPF's default PixelFormats.Bgra32 stride).
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            NativeMethods.BITMAPINFO bmi = default;
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = NativeMethods.BI_RGB;

            int rowsCopied = NativeMethods.GetDIBits(
                memDc,
                bitmap,
                0,
                (uint)height,
                pixels,
                ref bmi,
                NativeMethods.DIB_RGB_COLORS
            );
            if (rowsCopied == 0)
            {
                s_logCaptureFailed(_log, hwnd, "GetDIBits", null);
                return null;
            }

            return new ThumbnailSnapshot(new Size(width, height), pixels, stride);
        }
        finally
        {
            // Cleanup in reverse acquisition order. The previous-object
            // restore must happen before the bitmap is deleted, otherwise GDI
            // leaks the now-unowned bitmap. SelectObject returning IntPtr.Zero
            // means the original Select failed; we still try to restore for
            // symmetry, the call is harmless.
            if (memDc != IntPtr.Zero && previousObject != IntPtr.Zero)
            {
                _ = NativeMethods.SelectObject(memDc, previousObject);
            }
            if (bitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }
            if (memDc != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteDC(memDc);
            }
            if (windowDc != IntPtr.Zero)
            {
                _ = NativeMethods.ReleaseDC(hwnd, windowDc);
            }
        }
    }
}
