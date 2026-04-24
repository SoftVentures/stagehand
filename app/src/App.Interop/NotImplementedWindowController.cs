namespace App.Interop;

/// <summary>
/// Placeholder <see cref="IWindowController"/>. Every member throws
/// <see cref="NotImplementedException"/> until Plan 02 wires up Win32.
/// </summary>
public sealed class NotImplementedWindowController : IWindowController
{
    /// <inheritdoc />
    public void ParkOffscreen(IntPtr hwnd, Rect parkingRect) =>
        throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void RestorePosition(IntPtr hwnd, Rect original) =>
        throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void Resize(IntPtr hwnd, Rect target) =>
        throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void BringToFront(IntPtr hwnd) =>
        throw new NotImplementedException("Implemented in Plan 02.");
}
