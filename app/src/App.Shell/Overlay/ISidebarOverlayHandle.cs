using App.Core.Stage;

namespace App.Shell.Overlay;

/// <summary>
/// Thin lifecycle / sync seam over <see cref="SidebarOverlay"/>. Used by
/// <see cref="StageOverlayHost"/> so its tests can substitute a fake
/// without booting a real WPF Window.
/// </summary>
public interface ISidebarOverlayHandle
{
    /// <summary>Positions the overlay on the given HMONITOR and shows it.</summary>
    void ShowOn(IntPtr monitor);

    /// <summary>Disposes thumbnails and closes the underlying window.</summary>
    void HideAndRelease();

    /// <summary>Pushes a scene snapshot to the overlay.</summary>
    void SyncScenes(IReadOnlyList<Scene> scenes);

    /// <summary>Raised on tile click.</summary>
    event EventHandler<SidebarTileClickedEventArgs>? TileClicked;
}
