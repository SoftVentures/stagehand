using App.Core.Stage;
using App.Interop;

namespace App.Core.Layout;

/// <summary>
/// Input to <see cref="IThumbnailLayoutEngine.Compute"/>. Describes the sidebar
/// rectangle that the thumbnails live inside, the spacing/padding rules, the
/// per-thumbnail height cap and the source windows (with their pixel sizes as
/// reported by DWM) to lay out.
/// </summary>
/// <remarks>
/// The layout engine is a pure function (see Plan 02 §Design.7); this record
/// carries everything it needs. No references to the OS, DWM, or WPF.
/// </remarks>
/// <param name="SidebarBounds">
/// Destination area inside the overlay, in physical pixels. Origin may be
/// non-zero (sidebar docked to the right edge of a monitor whose origin is
/// not (0,0)).
/// </param>
/// <param name="ThumbnailSpacing">Vertical gap between stacked thumbnails, in pixels.</param>
/// <param name="OuterPadding">Padding between the sidebar edges and the thumbnails, in pixels.</param>
/// <param name="MaxThumbnailHeight">Upper cap for the height of any single thumbnail, in pixels.</param>
/// <param name="Windows">The source windows to lay out, with their current source sizes.</param>
public sealed record LayoutRequest(
    Rect SidebarBounds,
    double ThumbnailSpacing,
    double OuterPadding,
    double MaxThumbnailHeight,
    IReadOnlyList<(WindowIdentity Identity, Size SourceSize)> Windows
);
