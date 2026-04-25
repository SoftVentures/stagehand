using System.Diagnostics.CodeAnalysis;

namespace App.Interop;

/// <summary>
/// A static, in-memory snapshot of a window's content captured via
/// <c>PrintWindow</c> at park time. Rendered by
/// <see cref="BitmapThumbnailSource"/> when the source window is unsuitable
/// for live DWM mirroring (cloaked, minimised, DRM-protected).
/// </summary>
/// <param name="SourceSize">
/// Pixel dimensions of the source window at capture time, clamped to a 1×1
/// minimum to protect downstream layout math.
/// </param>
/// <param name="Bgra32Pixels">
/// Raw BGRA byte buffer (4 bytes per pixel; pre-multiplied alpha matches WPF's
/// default PixelFormats). Length is always
/// <c>SourceSize.Height * Stride</c>. Exposed as a <see cref="byte"/> array
/// because the WPF rendering layer feeds it directly into
/// <c>WriteableBitmap.WritePixels</c>, which takes the raw array — converting
/// to <c>ReadOnlyMemory&lt;byte&gt;</c> only forces an extra copy at every
/// repaint.
/// </param>
/// <param name="Stride">
/// Row stride in bytes, padded to a 4-byte boundary by GDI. Equal to
/// <c>SourceSize.Width * 4</c> for 32 bpp surfaces.
/// </param>
/// <remarks>
/// Snapshots are immutable value records; callers are expected to retain
/// them in memory only as long as the corresponding parked window exists.
/// Plan 05's <c>ParkTimeSnapshotCache</c> owns the lifetime in production.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1819:Properties should not return arrays",
    Justification = "Bgra32Pixels is a fixed-shape pixel buffer consumed verbatim by WriteableBitmap.WritePixels; wrapping it in ReadOnlyMemory<byte> would force a copy at every repaint."
)]
public sealed record ThumbnailSnapshot(Size SourceSize, byte[] Bgra32Pixels, int Stride);
