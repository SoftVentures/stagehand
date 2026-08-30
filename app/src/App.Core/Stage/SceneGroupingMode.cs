namespace App.Core.Stage;

/// <summary>
/// How <see cref="ISceneGrouper"/> assigns newly-enumerated windows to
/// scenes. Hard-coded to <see cref="ByProcessId"/> in Plan 03; Plan 04
/// surfaces the choice in the settings UI.
/// </summary>
public enum SceneGroupingMode
{
    /// <summary>
    /// Default. Windows whose owning <c>ProcessId</c> matches an existing
    /// scene's primary join that scene; otherwise a new scene is minted.
    /// Two Notepads from the same process land in one scene; one Notepad
    /// + one Calculator land in two scenes.
    /// </summary>
    ByProcessId,

    /// <summary>
    /// Every newly-enumerated window starts its own single-window scene.
    /// Users move windows between scenes via Plan 04's drag-and-drop UI.
    /// </summary>
    Manual,
}
