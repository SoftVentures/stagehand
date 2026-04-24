using App.Core.Stage;
using App.Interop;

namespace App.Core.Layout;

/// <summary>
/// Output of <see cref="IThumbnailLayoutEngine.Compute"/>: a single
/// window-to-destination-rectangle assignment. The consumer
/// (<c>SidebarOverlay.Sync</c>) feeds <see cref="DestinationRect"/> into
/// <c>DwmUpdateThumbnailProperties</c>.
/// </summary>
/// <param name="Identity">Stable identity of the source window.</param>
/// <param name="DestinationRect">The thumbnail target rectangle, in physical pixels, inside the sidebar.</param>
public sealed record ThumbnailPlacement(WindowIdentity Identity, Rect DestinationRect);
