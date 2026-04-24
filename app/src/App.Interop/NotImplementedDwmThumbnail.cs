namespace App.Interop;

/// <summary>
/// Placeholder <see cref="IDwmThumbnail"/>. Exposes an invalid safe-handle and
/// throws on every mutating call until Plan 02 lands.
/// </summary>
public sealed class NotImplementedDwmThumbnail : IDwmThumbnail
{
    /// <inheritdoc />
    public SafeDwmThumbnailHandle Handle { get; } = new();

    /// <inheritdoc />
    public void UpdateDestination(Rect destinationRect, byte opacity = 255) =>
        throw new NotImplementedException("Implemented in Plan 02.");

    /// <inheritdoc />
    public void Dispose() => Handle.Dispose();
}
