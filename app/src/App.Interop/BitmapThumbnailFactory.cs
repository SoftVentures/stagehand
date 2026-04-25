using App.Interop.Internal;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Production <see cref="IBitmapThumbnailFactory"/>. Pre-validates the source
/// window's bounds via <see cref="INativeWindowApi"/>, then delegates the full
/// PrintWindow / GetDIBits dance to <see cref="INativeBitmapApi"/>. The factory
/// itself is stateless; the <see cref="ILoggerFactory"/> is used to mint a
/// per-source logger so trace-level events (double-dispose) are categorised
/// correctly.
/// </summary>
public sealed class BitmapThumbnailFactory : IBitmapThumbnailFactory
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logDegenerate =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Debug,
            new EventId(1, nameof(BitmapThumbnailFactory) + ".Degenerate"),
            "Skipping bitmap capture for HWND 0x{Hwnd:X} (zero-or-negative window rect)."
        );

    private readonly INativeWindowApi _windowApi;
    private readonly INativeBitmapApi _bitmapApi;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BitmapThumbnailFactory> _log;

    /// <summary>
    /// Production constructor. Constructs both seams internally — same shape
    /// as <see cref="WindowController"/> and <see cref="DwmThumbnailFactory"/>
    /// so DI registration stays at one line.
    /// </summary>
    public BitmapThumbnailFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _windowApi = new NativeWindowApi();
        _bitmapApi = new NativeBitmapApi(loggerFactory.CreateLogger<NativeBitmapApi>());
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<BitmapThumbnailFactory>();
    }

    /// <summary>
    /// Test-facing constructor. Both seams are injected so the capture chain
    /// can be verified end-to-end without booting GDI.
    /// </summary>
    internal BitmapThumbnailFactory(
        INativeWindowApi windowApi,
        INativeBitmapApi bitmapApi,
        ILoggerFactory loggerFactory
    )
    {
        _windowApi = windowApi ?? throw new ArgumentNullException(nameof(windowApi));
        _bitmapApi = bitmapApi ?? throw new ArgumentNullException(nameof(bitmapApi));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger<BitmapThumbnailFactory>();
    }

    /// <inheritdoc />
    public ThumbnailSnapshot? Capture(IntPtr sourceHwnd)
    {
        Rect bounds = _windowApi.GetWindowRect(sourceHwnd);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            // Plan 02 §Design.6b step 1: degenerate windows (zero or negative
            // extent, e.g. mid-park) are skipped without an exception. Caller
            // falls back to live DWM or skips the tile entirely.
            s_logDegenerate(_log, sourceHwnd, null);
            return null;
        }

        return _bitmapApi.CaptureWindow(
            sourceHwnd,
            bounds.Width,
            bounds.Height,
            NativeMethods.PW_RENDERFULLCONTENT
        );
    }

    /// <inheritdoc />
    public BitmapThumbnailSource Register(IntPtr sourceHwnd, ThumbnailSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new BitmapThumbnailSource(
            sourceHwnd,
            snapshot,
            _loggerFactory.CreateLogger<BitmapThumbnailSource>()
        );
    }
}
