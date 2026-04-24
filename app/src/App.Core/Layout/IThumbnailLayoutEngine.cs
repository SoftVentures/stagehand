namespace App.Core.Layout;

/// <summary>
/// Pure geometry engine that maps a <see cref="LayoutRequest"/> to a list of
/// <see cref="ThumbnailPlacement"/>s describing where each live DWM thumbnail
/// should be drawn inside the sidebar overlay.
/// </summary>
/// <remarks>
/// Deliberately free of I/O, Win32 and WPF so the placement rules can be
/// unit-tested exhaustively (Plan 02 §Design.7).
/// </remarks>
public interface IThumbnailLayoutEngine
{
    /// <summary>
    /// Compute the per-window thumbnail rectangles for the given request.
    /// Returns an empty list when the request contains no windows.
    /// </summary>
    IReadOnlyList<ThumbnailPlacement> Compute(LayoutRequest request);
}
