namespace App.Interop;

/// <summary>
/// Captures bitmap snapshots of windows via <c>PrintWindow</c> and registers
/// them as <see cref="BitmapThumbnailSource"/> instances for the sidebar to
/// render when live DWM mirroring is unsuitable (cloaked, minimised,
/// DRM-protected).
/// </summary>
/// <remarks>
/// <para>
/// The only production implementation is <see cref="BitmapThumbnailFactory"/>.
/// Tests substitute a fake to script captures; the production factory wraps
/// the <see cref="Internal.INativeBitmapApi"/> seam so lifecycle / handle-leak
/// behaviour is verified against a real Win32 in the harness while pure
/// behavioural tests stay fast.
/// </para>
/// </remarks>
public interface IBitmapThumbnailFactory
{
    /// <summary>
    /// Captures the current pixel content of <paramref name="sourceHwnd"/>
    /// via <c>PrintWindow</c> with <c>PW_RENDERFULLCONTENT</c>. Returns
    /// <see langword="null"/> when the source window is degenerate (zero or
    /// negative width/height, e.g. an in-flight park) or when the capture
    /// chain fails.
    /// </summary>
    /// <remarks>
    /// Callers who want to register a thumbnail typically pair this with
    /// <see cref="Register"/>; the standalone capture path also supports
    /// future scenarios such as freeze-during-move previews.
    /// </remarks>
    ThumbnailSnapshot? Capture(IntPtr sourceHwnd);

    /// <summary>
    /// Wraps an existing <paramref name="snapshot"/> as a
    /// <see cref="BitmapThumbnailSource"/> bound to <paramref name="sourceHwnd"/>.
    /// The factory does not capture; the caller is responsible for supplying
    /// a non-null snapshot — usually one obtained via <see cref="Capture"/>.
    /// </summary>
    BitmapThumbnailSource Register(IntPtr sourceHwnd, ThumbnailSnapshot snapshot);
}
