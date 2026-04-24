using App.Interop;

namespace App.Services.Windows;

/// <summary>
/// Composition seam that combines <see cref="IWindowEnumerator"/> and
/// <see cref="App.Core.Stage.IWindowFilter"/> into the single query consumed by
/// Plan 03's <c>StageController</c>.
/// </summary>
/// <remarks>
/// Plan 02 §Design.3 sketches a single <c>WindowEnumerator</c> that owns both
/// snapshot construction and filter application. The Wave-1 layering split
/// moved <c>WindowFilter</c> into <see cref="App.Services.Windows"/> (so it can
/// observe <see cref="Settings.ISettingsService.Changed"/>) while the
/// enumerator remains in <c>App.Interop</c> next to the raw Win32 seam.
/// Because <c>App.Interop</c> cannot reference <c>App.Services</c>, this
/// service does the composition one layer up.
/// </remarks>
public interface IManageableWindowService
{
    /// <summary>
    /// Enumerates every top-level window and returns the subset accepted by
    /// the active <see cref="App.Core.Stage.IWindowFilter"/>.
    /// </summary>
    IReadOnlyList<WindowSnapshot> GetManageableWindows();
}
