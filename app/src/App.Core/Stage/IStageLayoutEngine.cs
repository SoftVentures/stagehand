using System.Collections.Immutable;

namespace App.Core.Stage;

/// <summary>
/// Computes the on-screen layout of window thumbnails inside the sidebar
/// rectangle given the set of candidate windows.
/// </summary>
public interface IStageLayoutEngine
{
    /// <summary>
    /// Compute the per-window target thumbnail rectangle for the given sidebar and window set.
    /// </summary>
    /// <param name="sidebarRect">The sidebar rectangle, in physical pixels.</param>
    /// <param name="windows">Candidate windows to render thumbnails for.</param>
    /// <returns>Mapping of HWND to its thumbnail target rectangle.</returns>
    ImmutableDictionary<IntPtr, App.Interop.Rect> ComputeThumbnailLayout(
        App.Interop.Rect sidebarRect,
        IReadOnlyList<App.Interop.WindowSnapshot> windows
    );
}
