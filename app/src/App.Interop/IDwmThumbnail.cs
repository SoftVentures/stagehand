namespace App.Interop;

/// <summary>
/// Managed wrapper around a DWM thumbnail registration. The thumbnail reflects
/// a live preview of a source HWND onto a destination HWND at a given rectangle.
/// One of the two <see cref="IThumbnailSource"/> implementations; chosen by
/// default and used for all visible / non-cloaked source windows.
/// </summary>
public interface IDwmThumbnail : IThumbnailSource
{
    /// <summary>
    /// Underlying <see cref="SafeDwmThumbnailHandle"/>. Exposed for tests and
    /// advanced scenarios; normal consumers should not touch it.
    /// </summary>
    SafeDwmThumbnailHandle Handle { get; }

    /// <summary>
    /// Sets or clears the source-crop rectangle. Passing <see langword="null"/>
    /// restores the default (full source window). The crop takes effect on
    /// the next <see cref="IThumbnailSource.UpdateDestinationRect"/> call.
    /// </summary>
    /// <param name="cropInSourcePixels">
    /// Crop rectangle in source-window pixel coordinates, or
    /// <see langword="null"/> to clear.
    /// </param>
    void SetSourceCrop(Rect? cropInSourcePixels);
}
