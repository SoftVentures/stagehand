using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Static-snapshot <see cref="IThumbnailSource"/>: holds a
/// <see cref="ThumbnailSnapshot"/> captured at park time and exposes the
/// destination rect / opacity that the WPF rendering layer should consume.
/// Used as a fallback when live DWM mirroring is unsuitable (cloaked,
/// minimised, DRM-protected).
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="DwmThumbnail"/>, this class does NOT render anything by
/// itself — DWM has no involvement. The bitmap is rendered by the WPF
/// sidebar layer (Plan 02 §Design.6b), which periodically polls
/// <see cref="DestinationRect"/> / <see cref="Opacity"/> / <see cref="Snapshot"/>
/// after every <see cref="UpdateDestinationRect"/> call. Plan 04 §S13a wires
/// the WPF host that consumes these properties; Plan 02 ships only the data
/// container so the abstraction is in place for Plan 05's cloak responder.
/// </para>
/// <para>
/// Instances are created exclusively by <see cref="BitmapThumbnailFactory"/>;
/// the constructor is <c>internal</c>. Disposal frees the in-memory pixel
/// buffer reference; the snapshot itself is GC-managed.
/// </para>
/// </remarks>
public sealed class BitmapThumbnailSource : IThumbnailSource
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logDoubleDispose =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Trace,
            new EventId(1, nameof(BitmapThumbnailSource) + ".DoubleDispose"),
            "BitmapThumbnailSource double-dispose ignored (source HWND 0x{SourceHwnd:X})."
        );

    private readonly ILogger<BitmapThumbnailSource> _log;

    private ThumbnailSnapshot? _snapshot;
    private Rect _destinationRect;
    private byte _opacity = 255;
    private bool _disposed;

    internal BitmapThumbnailSource(
        IntPtr source,
        ThumbnailSnapshot snapshot,
        ILogger<BitmapThumbnailSource> log
    )
    {
        Source = source;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public IntPtr Source { get; }

    /// <inheritdoc />
    public Size SourceSize => _snapshot?.SourceSize ?? Size.Empty;

    /// <summary>
    /// Captured pixel buffer plus stride / size metadata. Becomes
    /// <see langword="null"/> after <see cref="Dispose"/>; consumers that hold
    /// a reference past disposal must defend against that.
    /// </summary>
    public ThumbnailSnapshot? Snapshot => _snapshot;

    /// <summary>Last destination rect set via <see cref="UpdateDestinationRect"/>.</summary>
    public Rect DestinationRect => _destinationRect;

    /// <summary>Last opacity (0–255) set via <see cref="UpdateDestinationRect"/>.</summary>
    public byte Opacity => _opacity;

    /// <inheritdoc />
    public void UpdateDestinationRect(Rect destinationRect, byte opacity = 255)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _destinationRect = destinationRect;
        _opacity = opacity;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            s_logDoubleDispose(_log, Source, null);
            return;
        }

        _disposed = true;
        // Drop the snapshot reference so the GC can reclaim the (potentially
        // multi-megabyte) pixel buffer even if the source object itself
        // remains referenced briefly by a stale render observer.
        _snapshot = null;
    }
}
