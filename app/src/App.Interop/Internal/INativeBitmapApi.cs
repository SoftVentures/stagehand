namespace App.Interop.Internal;

/// <summary>
/// Thin, testable seam over the GDI-and-PrintWindow capture pipeline consumed
/// by <see cref="BitmapThumbnailFactory"/>. The interface is intentionally
/// flat — one call performs the full capture sequence (GetDC →
/// CreateCompatibleDC → CreateCompatibleBitmap → SelectObject → PrintWindow →
/// GetDIBits → cleanup) so tests can substitute a fake without inscriting the
/// six-call dance.
/// </summary>
/// <remarks>
/// <para>
/// The seam is <c>internal</c>; the Tests assembly accesses it via
/// <c>InternalsVisibleTo</c>. Production implementation:
/// <see cref="NativeBitmapApi"/>.
/// </para>
/// <para>
/// Hardware-accelerated content (Chromium, WPF, WinUI, DirectX games) requires
/// the <c>PW_RENDERFULLCONTENT</c> flag (Windows 8.1+). The capture call
/// surfaces the flags argument so tests can assert it; production callers
/// always pass <c>PW_RENDERFULLCONTENT</c>.
/// </para>
/// </remarks>
internal interface INativeBitmapApi
{
    /// <summary>
    /// Captures the full pixel content of <paramref name="hwnd"/> at the given
    /// <paramref name="width"/> × <paramref name="height"/> via
    /// <c>PrintWindow</c>, returning the result as a 32 bpp BGRA snapshot.
    /// </summary>
    /// <param name="hwnd">Source HWND. Caller must have already validated the
    /// window is alive and has non-zero size.</param>
    /// <param name="width">Capture width in physical pixels (≥ 1).</param>
    /// <param name="height">Capture height in physical pixels (≥ 1).</param>
    /// <param name="printWindowFlags">Flags forwarded verbatim to
    /// <c>PrintWindow</c>. Production passes <c>PW_RENDERFULLCONTENT</c>.</param>
    /// <returns>
    /// A captured snapshot, or <see langword="null"/> if any step in the
    /// capture chain failed (e.g. <c>PrintWindow</c> returned <c>FALSE</c>,
    /// <c>GetDIBits</c> returned 0). All intermediate GDI handles are
    /// guaranteed released regardless of success or failure.
    /// </returns>
    ThumbnailSnapshot? CaptureWindow(IntPtr hwnd, int width, int height, uint printWindowFlags);
}
