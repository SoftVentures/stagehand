namespace App.Core.Stage;

/// <summary>
/// Owns a per-monitor collection of sidebar overlays. The Stage controller
/// drives the lifecycle (<see cref="CreateForMonitor"/>,
/// <see cref="DisposeForMonitor"/>, <see cref="DisposeAll"/>) and pushes
/// scene snapshots through <see cref="SyncMonitor"/>; click / hover events
/// flow back via <see cref="SceneClicked"/>.
/// </summary>
/// <remarks>
/// Implemented in <c>App.Shell.Overlay.StageOverlayHost</c> (Plan 03 §S4).
/// The interface lives in <c>App.Core</c> so <c>StageController</c> can
/// depend on it without dragging WPF up into the domain layer.
/// </remarks>
public interface IStageOverlayHost
{
    /// <summary>HMONITORs currently managed by the host.</summary>
    IReadOnlyList<IntPtr> ManagedMonitors { get; }

    /// <summary>Creates an overlay for <paramref name="monitor"/> if absent.</summary>
    void CreateForMonitor(IntPtr monitor);

    /// <summary>Disposes the overlay for <paramref name="monitor"/> if present.</summary>
    void DisposeForMonitor(IntPtr monitor);

    /// <summary>
    /// Pushes <paramref name="scenes"/> to <paramref name="monitor"/>'s
    /// overlay. Each scene renders as one tile; multi-window scenes get a
    /// "+N" badge.
    /// </summary>
    void SyncMonitor(IntPtr monitor, IReadOnlyList<Scene> scenes);

    /// <summary>Disposes every overlay; called on <c>DisableAsync</c>.</summary>
    void DisposeAll();

    /// <summary>Raised when the user clicks a scene tile.</summary>
    event EventHandler<SceneClickedEventArgs>? SceneClicked;
}

/// <summary>Payload for <see cref="IStageOverlayHost.SceneClicked"/>.</summary>
/// <param name="Scene">Scene id resolved from the clicked tile.</param>
/// <param name="ClickedWindow">
/// The underlying window the user clicked. Multi-window scenes report the
/// hovered/clicked sub-window, not always the primary; useful for Plan 04
/// drag-and-drop UI.
/// </param>
/// <param name="DeviceName">Source monitor's GDI device name.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Matches Plan-03 spec. The type is an event payload; the EventArgs suffix accurately conveys that — the analyser's heuristic is overzealous for record types."
)]
public sealed record SceneClickedEventArgs(
    SceneId Scene,
    WindowIdentity ClickedWindow,
    string DeviceName
);
