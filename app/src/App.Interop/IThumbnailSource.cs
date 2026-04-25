namespace App.Interop;

/// <summary>
/// Abstraction over a sidebar tile's pixel source. Stagehand renders parked
/// windows in two ways:
/// <list type="bullet">
///   <item><description><b>Live DWM thumbnail</b> via <see cref="IDwmThumbnail"/> —
///   DWM streams the source window's surface directly into the destination
///   HWND. Cheap and real-time, but renders black for cloaked / minimised /
///   DRM-protected windows.</description></item>
///   <item><description><b>Static bitmap snapshot</b> via
///   <see cref="BitmapThumbnailSource"/> — captured once at park time via
///   <c>PrintWindow</c> and rendered by the WPF layer. Independent of the
///   source window's visibility state.</description></item>
/// </list>
/// Both implementations expose the same minimum surface so
/// <c>SidebarOverlay.Sync</c> and <c>ThumbnailLayoutEngine</c> remain oblivious
/// to the underlying technique. Plan 05's <c>CloakStateMonitor</c> swaps
/// between the two as a window's cloak state changes.
/// </summary>
/// <remarks>
/// See Plan 02 §Design.6, §6a, §6b for the full discussion. Acceptance
/// criterion #11 covers the abstraction's correctness.
/// </remarks>
public interface IThumbnailSource : IDisposable
{
    /// <summary>Source HWND being mirrored.</summary>
    IntPtr Source { get; }

    /// <summary>
    /// Source surface size in physical pixels. For DWM sources, queried lazily
    /// from <c>DwmQueryThumbnailSourceSize</c> on the first
    /// <see cref="UpdateDestinationRect"/>; for bitmap sources, fixed at the
    /// snapshot's capture-time size. Always clamps to a 1×1 minimum to protect
    /// downstream layout math from divide-by-zero. <see cref="Size.Empty"/>
    /// until the size has been observed.
    /// </summary>
    Size SourceSize { get; }

    /// <summary>
    /// Updates the on-screen destination rectangle and opacity at which the
    /// source is rendered. For DWM sources this calls
    /// <c>DwmUpdateThumbnailProperties</c>; for bitmap sources it stores the
    /// rect for the WPF rendering layer to consume.
    /// </summary>
    /// <param name="destinationRect">Destination rectangle in physical pixels,
    /// relative to the destination HWND's client area.</param>
    /// <param name="opacity">0 (transparent) to 255 (opaque). Defaults to fully
    /// opaque.</param>
    void UpdateDestinationRect(Rect destinationRect, byte opacity = 255);
}
