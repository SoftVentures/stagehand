using System.ComponentModel;
using System.Runtime.InteropServices;
using App.Interop;
using App.Interop.Errors;
using Microsoft.Extensions.Logging;
using Rect = App.Interop.Rect;

namespace App.Shell.Overlay;

/// <summary>
/// Snapshot of an HMONITOR's geometry and DPI scale, resolved ad-hoc via
/// <c>GetMonitorInfoW</c> + <c>GetDpiForMonitor</c>.
/// </summary>
/// <remarks>
/// Plan 02 needs a single <c>SidebarOverlay</c> on the primary monitor and
/// therefore only this inline helper; the richer <c>MonitorEnumerator</c>
/// arrives in Plan 03 and will replace it.
/// <para>
/// <see cref="Bounds"/> and <see cref="WorkArea"/> are in physical pixels,
/// matching the Win32 conventions. <see cref="DpiScale"/> is the WPF scale
/// factor (effective DPI / 96) — divide a physical-pixel value by it to
/// obtain the equivalent device-independent unit (DIP) value.
/// </para>
/// </remarks>
/// <param name="Handle">HMONITOR the snapshot was resolved from.</param>
/// <param name="WorkArea">The work area (full bounds minus taskbar) in physical pixels.</param>
/// <param name="Bounds">Full monitor bounds in physical pixels.</param>
/// <param name="DpiScale">WPF scale factor (effective DPI ÷ 96).</param>
public sealed record OverlayMonitor(IntPtr Handle, Rect WorkArea, Rect Bounds, double DpiScale)
{
    private static readonly Action<ILogger, IntPtr, int, Exception?> s_logDpiFallback =
        LoggerMessage.Define<IntPtr, int>(
            LogLevel.Debug,
            new EventId(6001, nameof(OverlayMonitor) + ".DpiFallback"),
            "OverlayMonitor: GetDpiForMonitor(0x{HMonitor:X}) failed with HRESULT 0x{HResult:X8}; falling back to scale 1.0."
        );

    /// <summary>
    /// Resolves an <see cref="OverlayMonitor"/> from an HMONITOR.
    /// </summary>
    /// <param name="hmonitor">The HMONITOR to resolve.</param>
    /// <param name="log">
    /// Optional logger used to surface the HRESULT when
    /// <c>GetDpiForMonitor</c> falls back to 1.0. When <see langword="null"/>
    /// the fallback is silent (the overlay still recovers on the next
    /// <c>WM_DPICHANGED</c>).
    /// </param>
    /// <exception cref="Win32InteropException">
    /// <c>GetMonitorInfoW</c> returned <see langword="false"/>.
    /// </exception>
    public static OverlayMonitor Resolve(IntPtr hmonitor, ILogger? log = null)
    {
        var info = new NativeMethods.MONITORINFOEXW
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
        };

        if (!NativeMethods.GetMonitorInfo(hmonitor, ref info))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32InteropException(
                err,
                $"GetMonitorInfoW failed (Win32 error {err}).",
                new Win32Exception(err)
            );
        }

        Rect bounds = new(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top
        );

        Rect workArea = new(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left,
            info.rcWork.Bottom - info.rcWork.Top
        );

        // IDE0042 (deconstruct) fights IDE0007/IDE0008 (var vs explicit) on
        // the deconstructed form — keep it as a named-tuple access instead.
#pragma warning disable IDE0042
        (double scale, int? hresult) dpi = PerMonitorDpiHelpers.DpiScaleForMonitor(hmonitor);
#pragma warning restore IDE0042
        if (dpi.hresult is { } hr && log is not null)
        {
            s_logDpiFallback(log, hmonitor, hr, null);
        }
        return new OverlayMonitor(hmonitor, workArea, bounds, dpi.scale);
    }
}
