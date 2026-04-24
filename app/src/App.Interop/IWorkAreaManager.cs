namespace App.Interop;

/// <summary>
/// Reads and temporarily overrides the Windows work area (the portion of a
/// monitor not occupied by the taskbar / AppBars). Stagehand reserves a strip
/// for its dock by shrinking the work area so maximised windows respect it.
/// </summary>
public interface IWorkAreaManager
{
    /// <summary>
    /// Returns the current work-area rectangle for the specified monitor, in
    /// physical pixels.
    /// </summary>
    /// <param name="monitorHandle">Target HMONITOR.</param>
    Rect GetWorkArea(IntPtr monitorHandle);

    /// <summary>
    /// Sets the work-area rectangle for the specified monitor. Must be called
    /// from a thread with an active message pump so SPI broadcasts succeed.
    /// </summary>
    /// <param name="monitorHandle">Target HMONITOR.</param>
    /// <param name="area">Desired work area in physical pixels.</param>
    void SetWorkArea(IntPtr monitorHandle, Rect area);

    /// <summary>
    /// Restores every monitor's original work area recorded during startup.
    /// Called on shutdown so Stagehand never leaves the shell in a modified state.
    /// </summary>
    void RestoreAll();
}
