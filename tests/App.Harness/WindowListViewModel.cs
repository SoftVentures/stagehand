using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using App.Interop;
using App.Services.Windows;
using Microsoft.Extensions.Logging;
using Rect = App.Interop.Rect;

namespace App.Harness;

/// <summary>
/// Thin view-model backing the harness's window-list ListBox. Plan 02 §S9.
/// </summary>
/// <remarks>
/// <para>
/// Not a full MVVM implementation — the harness is a manual test runner,
/// not a production surface. <see cref="ObservableCollection{T}"/> provides
/// the change-notification the ListBox needs; no <c>INotifyPropertyChanged</c>
/// scaffolding is required for the handful of scalar properties.
/// </para>
/// <para>
/// <see cref="ParkedOriginalBounds"/> caches the pre-park bounds keyed on the
/// HWND of the target window, so the "Restore" button can round-trip through
/// <see cref="IWindowController.RestorePosition"/> without having to re-query
/// the original geometry.
/// </para>
/// <para>
/// <b>Previews.</b> Each <see cref="Refresh"/> call captures a static
/// <c>PrintWindow</c> snapshot per manageable window via
/// <see cref="IBitmapThumbnailFactory.Capture"/> and converts the
/// <see cref="ThumbnailSnapshot"/> into a frozen
/// <see cref="WriteableBitmap"/> for the ListBox <c>ItemTemplate</c>. Capture
/// failures (degenerate rects, DRM-protected content, hardware-accelerated
/// surfaces that <c>PrintWindow</c> can't reach) leave the preview
/// <see langword="null"/>; the template renders an empty placeholder for
/// those rows.
/// </para>
/// </remarks>
public sealed class WindowListViewModel
{
    private static readonly Action<ILogger, nint, Exception?> s_logPreviewSkipped =
        LoggerMessage.Define<nint>(
            LogLevel.Debug,
            new EventId(7001, "HarnessPreviewSkipped"),
            "Preview capture skipped or failed for HWND 0x{Hwnd:X}."
        );

    private readonly IManageableWindowService _service;
    private readonly IBitmapThumbnailFactory _bitmapFactory;
    private readonly ILogger<WindowListViewModel> _log;

    public WindowListViewModel(
        IManageableWindowService service,
        IBitmapThumbnailFactory bitmapFactory,
        ILogger<WindowListViewModel> log
    )
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _bitmapFactory = bitmapFactory ?? throw new ArgumentNullException(nameof(bitmapFactory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Current snapshot list — bound to the ListBox.</summary>
    public ObservableCollection<WindowListItem> Windows { get; } = [];

    /// <summary>
    /// Pre-park bounds cache keyed on HWND. Populated by the "Park" button,
    /// consumed by "Restore".
    /// </summary>
    public Dictionary<nint, Rect> ParkedOriginalBounds { get; } = [];

    /// <summary>
    /// Re-enumerates manageable windows, captures a preview per window, and
    /// replaces the <see cref="Windows"/> collection. Called by the
    /// "Refresh" button and by the WinEvent-hook-driven auto-refresh.
    /// </summary>
    public IReadOnlyList<WindowSnapshot> Refresh()
    {
        IReadOnlyList<WindowSnapshot> snapshots = _service.GetManageableWindows();
        Windows.Clear();
        foreach (WindowSnapshot snap in snapshots)
        {
            ImageSource? preview = TryCapturePreview(snap.Hwnd);
            Windows.Add(new WindowListItem(snap, preview));
        }
        return snapshots;
    }

    private WriteableBitmap? TryCapturePreview(nint hwnd)
    {
        try
        {
            ThumbnailSnapshot? snapshot = _bitmapFactory.Capture(hwnd);
            if (snapshot is null)
            {
                s_logPreviewSkipped(_log, hwnd, null);
                return null;
            }

            // 32 bpp BGRA top-down (NativeBitmapApi feeds GetDIBits a negative
            // biHeight so rows are emitted top-to-bottom). WriteableBitmap with
            // PixelFormats.Bgra32 expects exactly that layout. Freezing makes
            // the bitmap thread-safe and lets WPF skip ownership tracking.
            var bitmap = new WriteableBitmap(
                snapshot.SourceSize.Width,
                snapshot.SourceSize.Height,
                dpiX: 96,
                dpiY: 96,
                pixelFormat: PixelFormats.Bgra32,
                palette: null
            );
            bitmap.WritePixels(
                new Int32Rect(0, 0, snapshot.SourceSize.Width, snapshot.SourceSize.Height),
                snapshot.Bgra32Pixels,
                snapshot.Stride,
                offset: 0
            );
            bitmap.Freeze();
            return bitmap;
        }
#pragma warning disable CA1031 // Preview capture must never crash the harness — log and degrade.
        catch (Exception ex)
        {
            s_logPreviewSkipped(_log, hwnd, ex);
            return null;
        }
#pragma warning restore CA1031
    }
}
