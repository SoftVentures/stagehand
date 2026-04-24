namespace App.Interop;

/// <summary>
/// Integer-pixel size value type used across the interop boundary.
/// </summary>
/// <remarks>
/// Kept deliberately pure — no dependencies on WPF or <c>System.Drawing</c>.
/// Pair with <see cref="Rect"/> for device-space geometry.
/// </remarks>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct Size(int Width, int Height)
{
    /// <summary>Empty size — width and height are both zero.</summary>
    public static readonly Size Empty;

    /// <summary>True when both <see cref="Width"/> and <see cref="Height"/> are zero.</summary>
    public bool IsEmpty => Width == 0 && Height == 0;
}
