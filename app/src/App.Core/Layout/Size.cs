namespace App.Core.Layout;

/// <summary>
/// Two-dimensional size value type used by the layout engine to describe the
/// pixel dimensions of a source window (as reported by
/// <c>DwmQueryThumbnailSourceSize</c>).
/// </summary>
/// <remarks>
/// Kept deliberately pure — no dependency on <c>System.Windows</c> so that
/// <c>App.Core</c> can remain free of WPF references (see
/// <c>app/CLAUDE.md</c> — per-project guardrails).
/// </remarks>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct Size(double Width, double Height)
{
    /// <summary>Zero-sized instance.</summary>
    public static readonly Size Empty;

    /// <summary>
    /// True when both dimensions are exactly zero. Mirrors the
    /// <see cref="App.Interop.Size.IsEmpty"/> helper so call sites can use
    /// either type interchangeably.
    /// </summary>
    public bool IsEmpty => Width == 0 && Height == 0;
}
