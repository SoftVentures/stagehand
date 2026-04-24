namespace App.Interop;

/// <summary>
/// Performs position/size/z-order operations on external HWNDs.
/// Consumers must pass HWNDs freshly validated via <see cref="IWindowEnumerator"/>
/// (or by checking <c>IsWindow</c>) — HWNDs can die between calls.
/// </summary>
public interface IWindowController
{
    /// <summary>
    /// Moves <paramref name="hwnd"/> to the off-screen parking rectangle without
    /// activating it. Preserves size by default.
    /// </summary>
    /// <param name="hwnd">Target window handle.</param>
    /// <param name="parkingRect">Destination rectangle in physical pixels.</param>
    void ParkOffscreen(IntPtr hwnd, Rect parkingRect);

    /// <summary>
    /// Restores <paramref name="hwnd"/> to the given original rectangle
    /// (typically recorded before <see cref="ParkOffscreen"/>).
    /// </summary>
    /// <param name="hwnd">Target window handle.</param>
    /// <param name="original">Rectangle to restore to, in physical pixels.</param>
    void RestorePosition(IntPtr hwnd, Rect original);

    /// <summary>
    /// Resizes and/or moves <paramref name="hwnd"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="hwnd">Target window handle.</param>
    /// <param name="target">Destination rectangle in physical pixels.</param>
    void Resize(IntPtr hwnd, Rect target);

    /// <summary>
    /// Activates <paramref name="hwnd"/> and brings it to the foreground.
    /// May require <c>AttachThreadInput</c> to overcome foreground-lock rules.
    /// </summary>
    /// <param name="hwnd">Target window handle.</param>
    void BringToFront(IntPtr hwnd);
}
