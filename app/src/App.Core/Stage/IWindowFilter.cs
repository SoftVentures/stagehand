namespace App.Core.Stage;

/// <summary>
/// Policy that decides whether a given enumerated window is eligible for Stage
/// management (parking, thumbnailing, focus). Implementations should be pure
/// and side-effect free.
/// </summary>
public interface IWindowFilter
{
    /// <summary>
    /// Returns <see langword="true"/> if the given window snapshot represents a
    /// window that Stagehand can and should manage.
    /// </summary>
    bool IsManageable(App.Interop.WindowSnapshot snapshot);
}
