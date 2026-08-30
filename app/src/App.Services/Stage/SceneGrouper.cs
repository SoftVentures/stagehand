using App.Core.Stage;
using App.Interop;

namespace App.Services.Stage;

/// <summary>
/// Production <see cref="ISceneGrouper"/> implementation. Stateless;
/// safe to register as a singleton.
/// </summary>
public sealed class SceneGrouper : ISceneGrouper
{
    /// <inheritdoc />
    public SceneAssignment AssignToScene(
        WindowSnapshot newWindow,
        IReadOnlyList<Scene> existingScenesOnSameMonitor,
        SceneGroupingMode mode
    )
    {
        ArgumentNullException.ThrowIfNull(existingScenesOnSameMonitor);

        if (mode == SceneGroupingMode.Manual)
        {
            return new SceneAssignment(SceneId.New(), CreatedNewScene: true);
        }

        // ByProcessId: first match wins. Chromium-based browsers (Edge,
        // Chrome) spawn a fresh top-level process per window, so PID-only
        // matching makes every window its own scene — visually a duplicate
        // tile per window. Match ProcessName first when both sides carry
        // one; fall back to PID for legacy Win32 apps with shared
        // processes (Notepad, Explorer, etc.).
        foreach (Scene scene in existingScenesOnSameMonitor)
        {
            if (
                !string.IsNullOrEmpty(newWindow.ProcessName)
                && !string.IsNullOrEmpty(scene.ProcessName)
                && string.Equals(
                    scene.ProcessName,
                    newWindow.ProcessName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return new SceneAssignment(scene.Id, CreatedNewScene: false);
            }
            if (scene.Primary.ProcessId == newWindow.ProcessId)
            {
                return new SceneAssignment(scene.Id, CreatedNewScene: false);
            }
        }
        return new SceneAssignment(SceneId.New(), CreatedNewScene: true);
    }
}
