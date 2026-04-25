namespace App.Interop;

/// <summary>
/// Production <see cref="IThumbnailSourceFactory"/>. Pure delegation to the
/// two underlying factories — no logic, no state. Exists so callers can take
/// a single dependency on the composite seam instead of two.
/// </summary>
public sealed class ThumbnailSourceFactory : IThumbnailSourceFactory
{
    private readonly IDwmThumbnailFactory _dwm;
    private readonly IBitmapThumbnailFactory _bitmap;

    public ThumbnailSourceFactory(IDwmThumbnailFactory dwm, IBitmapThumbnailFactory bitmap)
    {
        _dwm = dwm ?? throw new ArgumentNullException(nameof(dwm));
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
    }

    /// <inheritdoc />
    public IDwmThumbnail RegisterDwm(IntPtr sourceHwnd, IntPtr destinationHwnd) =>
        _dwm.Register(sourceHwnd, destinationHwnd);

    /// <inheritdoc />
    public BitmapThumbnailSource RegisterBitmap(IntPtr sourceHwnd, ThumbnailSnapshot snapshot) =>
        _bitmap.Register(sourceHwnd, snapshot);

    /// <inheritdoc />
    public ThumbnailSnapshot? Capture(IntPtr sourceHwnd) => _bitmap.Capture(sourceHwnd);
}
