namespace App.Core.Stage;

/// <summary>
/// Plan describing how to swap one scene with another on a specific
/// monitor. Built by <c>StageController.SwapAsync</c> and handed to
/// <see cref="ISceneSwapExecutor.RunAsync"/> which performs the actual
/// Win32 calls.
/// </summary>
/// <param name="IncomingSceneId">The scene becoming active.</param>
/// <param name="IncomingMoves">
/// One <see cref="SceneWindowMove"/> per window in the incoming scene —
/// from its sidebar slot to the main area.
/// </param>
/// <param name="OutgoingSceneId">
/// The previously-active scene id, or <see langword="null"/> when no
/// scene was active on this monitor (the main area showed wallpaper).
/// </param>
/// <param name="OutgoingMoves">
/// One <see cref="SceneWindowMove"/> per window in the outgoing scene —
/// from the main area to the sidebar slot. Empty when
/// <paramref name="OutgoingSceneId"/> is <see langword="null"/>.
/// </param>
/// <param name="TargetDeviceName">GDI device name of the affected monitor.</param>
/// <param name="IncomingPrimary">
/// Identity of the incoming scene's primary; brought to the front after
/// every move completes.
/// </param>
public sealed record SceneSwapPlan(
    SceneId IncomingSceneId,
    IReadOnlyList<SceneWindowMove> IncomingMoves,
    SceneId? OutgoingSceneId,
    IReadOnlyList<SceneWindowMove> OutgoingMoves,
    string TargetDeviceName,
    WindowIdentity IncomingPrimary
);
