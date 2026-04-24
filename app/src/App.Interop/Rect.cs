namespace App.Interop;

/// <summary>
/// Rectangle value type used across the interop boundary.
/// Uses integer (pixel) coordinates in physical/device space.
/// </summary>
/// <remarks>
/// Kept deliberately pure — no dependencies on WPF or <c>System.Drawing</c>.
/// <see cref="System.Drawing.Rectangle"/> conversion helpers will be added in
/// Plan 02 when the first consumer (e.g. <c>WindowEnumerator</c>) needs them.
/// </remarks>
/// <param name="X">Left edge in pixels.</param>
/// <param name="Y">Top edge in pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>Empty rectangle — all fields zero.</summary>
    public static readonly Rect Empty;

    /// <summary>Left edge (alias for <see cref="X"/>).</summary>
    public int Left => X;

    /// <summary>Top edge (alias for <see cref="Y"/>).</summary>
    public int Top => Y;

    /// <summary>Right edge, exclusive. Equivalent to <c>X + Width</c>.</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge, exclusive. Equivalent to <c>Y + Height</c>.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when both <see cref="Width"/> and <see cref="Height"/> are zero.</summary>
    public bool IsEmpty => Width == 0 && Height == 0;
}
