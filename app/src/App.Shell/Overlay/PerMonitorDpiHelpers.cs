using System.Runtime.InteropServices;
using App.Interop;

namespace App.Shell.Overlay;

/// <summary>
/// Small helpers for translating between Win32 DPI integers and WPF's
/// device-independent-pixel (DIP) scale factor.
/// </summary>
/// <remarks>
/// WPF expresses window coordinates in DIPs (1 DIP == 1/96 inch at 100 %
/// scaling). The Win32 monitor and window APIs return physical pixels and
/// a DPI integer. The overlay needs both: the <em>sidebar bounds</em> that
/// the layout engine consumes must be physical pixels (so DWM thumbnails
/// land on pixel boundaries), while the window itself has to be positioned
/// in DIPs.
/// <para>
/// This class is intentionally tiny so <c>SidebarOverlay</c> keeps the
/// unit-conversion arithmetic legible. Plan 04 may fold it into a richer
/// <c>IDpiProvider</c> seam; for now it's static.
/// </para>
/// </remarks>
internal static class PerMonitorDpiHelpers
{
    /// <summary>
    /// Default Win32 DPI value corresponding to WPF's 1.0 scale factor.
    /// </summary>
    public const uint DefaultDpi = 96;

    /// <summary>
    /// Returns the DPI for the monitor that hosts <paramref name="hwnd"/>.
    /// </summary>
    /// <remarks>
    /// Requires Windows 10 1607 or newer — guaranteed by the project's TFM
    /// (<c>net8.0-windows</c>).
    /// </remarks>
    public static uint GetDpiForWindow(IntPtr hwnd)
    {
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi == 0 ? DefaultDpi : dpi;
    }

    /// <summary>Converts a Win32 DPI integer to a WPF scale factor.</summary>
    public static double DpiScaleFromDpi(uint dpi) => dpi / (double)DefaultDpi;

    /// <summary>
    /// Queries <c>GetDpiForMonitor(MDT_EFFECTIVE_DPI)</c> and returns the
    /// horizontal component as a WPF scale factor. Falls back to 1.0 on
    /// failure; the returned <c>hresult</c> is non-null when the fallback
    /// was taken so the caller can decide whether to log.
    /// </summary>
    /// <returns>
    /// A tuple of the WPF scale factor and an optional HRESULT. When the
    /// native call succeeds the HRESULT is <see langword="null"/>; when it
    /// throws <see cref="COMException"/> the thrown HRESULT is surfaced so
    /// the caller can log it without wrapping a second <c>try/catch</c>.
    /// </returns>
    public static (double Scale, int? HResult) DpiScaleForMonitor(IntPtr hmonitor)
    {
        try
        {
            NativeMethods.GetDpiForMonitor(
                hmonitor,
                NativeMethods.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI,
                out var dpiX,
                out _
            );
            return (dpiX == 0 ? 1.0 : DpiScaleFromDpi(dpiX), null);
        }
        catch (COMException ex)
        {
            // shcore returns E_INVALIDARG when the HMONITOR has been
            // invalidated by a topology change. The overlay recovers on the
            // next WM_DPICHANGED. Surface the HRESULT so the caller can log.
            return (1.0, ex.HResult);
        }
    }
}
