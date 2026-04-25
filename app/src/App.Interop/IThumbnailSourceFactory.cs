namespace App.Interop;

/// <summary>
/// Composite factory that exposes both thumbnail-source kinds (DWM and
/// bitmap) behind a single seam, so that callers — primarily the cloak
/// responder introduced in Plan 05 — can switch implementations per source
/// HWND without taking a dependency on either concrete factory.
/// </summary>
/// <remarks>
/// The default registration in <c>ServiceConfiguration</c> wires
/// <see cref="ThumbnailSourceFactory"/> with the production
/// <see cref="DwmThumbnailFactory"/> and <see cref="BitmapThumbnailFactory"/>
/// underneath. Tests can substitute either factory in isolation.
/// </remarks>
public interface IThumbnailSourceFactory
{
    /// <summary>
    /// Registers a live DWM thumbnail (the default rendering path).
    /// </summary>
    /// <inheritdoc cref="IDwmThumbnailFactory.Register"/>
    IDwmThumbnail RegisterDwm(IntPtr sourceHwnd, IntPtr destinationHwnd);

    /// <summary>
    /// Wraps an existing <paramref name="snapshot"/> as a
    /// <see cref="BitmapThumbnailSource"/>. Used as a fallback when DWM cannot
    /// render the source (cloaked / minimised / DRM-protected).
    /// </summary>
    BitmapThumbnailSource RegisterBitmap(IntPtr sourceHwnd, ThumbnailSnapshot snapshot);

    /// <summary>
    /// Captures the current pixel content of the source window via
    /// <c>PrintWindow</c> for later <see cref="RegisterBitmap"/> consumption.
    /// Returns <see langword="null"/> on degenerate or failed capture; see
    /// <see cref="IBitmapThumbnailFactory.Capture"/> for details.
    /// </summary>
    ThumbnailSnapshot? Capture(IntPtr sourceHwnd);
}
