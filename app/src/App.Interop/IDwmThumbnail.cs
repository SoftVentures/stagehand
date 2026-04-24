namespace App.Interop;

/// <summary>
/// Managed wrapper around a DWM thumbnail registration. The thumbnail reflects
/// a live preview of a source HWND onto a destination HWND at a given rectangle.
/// </summary>
public interface IDwmThumbnail : IDisposable
{
    /// <summary>
    /// Underlying <see cref="SafeDwmThumbnailHandle"/>. Exposed for tests and
    /// advanced scenarios; normal consumers should not touch it.
    /// </summary>
    SafeDwmThumbnailHandle Handle { get; }

    /// <summary>
    /// Updates the on-screen destination rectangle and opacity of the thumbnail.
    /// </summary>
    /// <param name="destinationRect">Destination rectangle in physical pixels, relative to the destination HWND's client area.</param>
    /// <param name="opacity">0 (transparent) to 255 (opaque). Defaults to fully opaque.</param>
    void UpdateDestinationRect(Rect destinationRect, byte opacity = 255);

    /// <summary>
    /// Sets or clears the source-crop rectangle. Passing <see langword="null"/>
    /// restores the default (full source window). The crop takes effect on
    /// the next <see cref="UpdateDestinationRect"/> call.
    /// </summary>
    /// <param name="cropInSourcePixels">
    /// Crop rectangle in source-window pixel coordinates, or
    /// <see langword="null"/> to clear.
    /// </param>
    void SetSourceCrop(Rect? cropInSourcePixels);
}
