namespace App.Interop;

/// <summary>
/// Enumerates top-level windows on the current desktop that Stagehand is
/// permitted to manage (visible, non-cloaked, non-shell, user-owned).
/// </summary>
public interface IWindowEnumerator
{
    /// <summary>
    /// Returns a point-in-time snapshot of all manageable top-level windows.
    /// Filters out cloaked UWP hosts, the shell, Stagehand's own windows, and
    /// anything without a visible root.
    /// </summary>
    IReadOnlyList<WindowSnapshot> GetManageableWindows();
}
