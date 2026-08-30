namespace App.Core.Stage;

/// <summary>
/// Result of <see cref="ISceneGrouper.AssignToScene"/>: the scene id the
/// caller should add the window to, plus a flag indicating whether the
/// scene is brand-new (caller must mint a fresh <see cref="Scene"/>) or
/// existing (caller appends the window to the matching scene's window
/// list).
/// </summary>
/// <param name="TargetScene">Scene id to associate the window with.</param>
/// <param name="CreatedNewScene">
/// <see langword="true"/> when the grouper minted a fresh
/// <see cref="SceneId"/> via <see cref="SceneId.New"/>; the caller must
/// initialise a new <see cref="Scene"/> with the window as its primary.
/// <see langword="false"/> when an existing scene matched.
/// </param>
public readonly record struct SceneAssignment(SceneId TargetScene, bool CreatedNewScene);
