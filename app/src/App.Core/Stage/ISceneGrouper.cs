using App.Interop;

namespace App.Core.Stage;

/// <summary>
/// Stateless pure-function service that decides which scene a freshly
/// enumerated window should join.
/// </summary>
/// <remarks>
/// <para>
/// The grouper is monitor-scoped: callers pass only the scenes that
/// already exist on the same monitor as the window. Cross-monitor scene
/// reuse is the controller's job, not the grouper's.
/// </para>
/// <para>
/// Identity-based dedupe (a window already a member of <em>any</em> scene)
/// is also the caller's responsibility — the grouper assumes
/// <paramref name="newWindow"/> is genuinely new.
/// </para>
/// </remarks>
public interface ISceneGrouper
{
    /// <summary>
    /// Returns the scene id the window should be assigned to, plus a flag
    /// indicating whether the scene is freshly minted.
    /// </summary>
    /// <param name="newWindow">
    /// The window being enumerated. The grouper inspects
    /// <see cref="WindowSnapshot.ProcessId"/> only.
    /// </param>
    /// <param name="existingScenesOnSameMonitor">
    /// Scenes already on <paramref name="newWindow"/>'s monitor, ordered
    /// by <see cref="Scene.CreatedAt"/> ascending. The grouper walks this
    /// list in order and returns the first match (by mode-specific
    /// criteria); when none matches it mints a new id.
    /// </param>
    /// <param name="mode">Grouping policy.</param>
    SceneAssignment AssignToScene(
        WindowSnapshot newWindow,
        IReadOnlyList<Scene> existingScenesOnSameMonitor,
        SceneGroupingMode mode
    );
}
