using App.Interop.Internal;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Managed wrapper around a single DWM thumbnail registration. Owns the
/// <see cref="SafeDwmThumbnailHandle"/> and marshals every native call
/// through the testable <see cref="INativeDwmApi"/> seam.
/// </summary>
/// <remarks>
/// <para>
/// Instances are created exclusively by <see cref="DwmThumbnailFactory"/>;
/// the constructor is <c>internal</c> so callers cannot bypass the factory.
/// Every public method asserts UI-thread affinity — DWM thumbnail APIs are
/// not thread-safe against arbitrary callers, and the destination HWND
/// always lives on the WPF dispatcher thread.
/// </para>
/// <para>
/// <b>Dispose semantics.</b> <see cref="Dispose"/> is idempotent: a second
/// call logs at <c>Trace</c> and returns. The underlying
/// <see cref="SafeDwmThumbnailHandle"/> calls
/// <c>DwmUnregisterThumbnail</c> directly from its <c>ReleaseHandle</c>
/// override, so the handle is released even if <see cref="Dispose"/> is
/// never called (finalizer path).
/// </para>
/// </remarks>
public sealed class DwmThumbnail : IDwmThumbnail
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logDoubleDispose =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Trace,
            new EventId(1, nameof(DwmThumbnail) + ".DoubleDispose"),
            "DwmThumbnail double-dispose ignored (source HWND 0x{SourceHwnd:X})."
        );

    private readonly INativeDwmApi _native;
    private readonly UiDispatcher _ui;
    private readonly ILogger<DwmThumbnail> _log;

    private Rect? _sourceCrop;
    private bool _hasQueriedSourceSize;
    private bool _disposed;

    internal DwmThumbnail(
        SafeDwmThumbnailHandle handle,
        IntPtr source,
        IntPtr destination,
        INativeDwmApi native,
        UiDispatcher ui,
        ILogger<DwmThumbnail> log
    )
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        Source = source;
        Destination = destination;
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public SafeDwmThumbnailHandle Handle { get; }

    /// <summary>Source HWND (the window being mirrored).</summary>
    public IntPtr Source { get; }

    /// <summary>Destination HWND (usually the sidebar overlay).</summary>
    public IntPtr Destination { get; }

    /// <summary>
    /// Source window size in physical pixels, as reported by DWM the first
    /// time <see cref="UpdateDestinationRect"/> ran. Clamped to a 1×1 minimum
    /// to protect downstream layout math from divide-by-zero.
    /// <see cref="Interop.Size.Empty"/> until the first update.
    /// </summary>
    public Size SourceSize { get; private set; }

    /// <inheritdoc />
    public void UpdateDestinationRect(Rect destinationRect, byte opacity = 255)
    {
        _ui.AssertOnUiThread();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_hasQueriedSourceSize)
        {
            Size raw = _native.DwmQueryThumbnailSourceSize(Handle.DangerousGetHandle());
            // Clamp to a 1×1 minimum — DWM occasionally returns zero for newly-
            // created or minimised windows; downstream layout divides by these
            // numbers to compute aspect ratios.
            SourceSize = new Size(Math.Max(1, raw.Width), Math.Max(1, raw.Height));
            _hasQueriedSourceSize = true;
        }

        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            // Single update call — never unregister/reregister (avoids flicker,
            // see Plan 02 §Risks row 1).
            dwFlags =
                NativeMethods.DWM_TNP_RECTDESTINATION
                | NativeMethods.DWM_TNP_VISIBLE
                | NativeMethods.DWM_TNP_OPACITY
                | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = ToNative(destinationRect),
            opacity = opacity,
            fVisible = true,
            fSourceClientAreaOnly = true,
        };

        if (_sourceCrop is { } crop)
        {
            props.dwFlags |= NativeMethods.DWM_TNP_RECTSOURCE;
            props.rcSource = ToNative(crop);
        }

        _native.DwmUpdateThumbnailProperties(Handle.DangerousGetHandle(), in props);
    }

    /// <summary>
    /// Sets or clears the source-crop rectangle. Passing <see langword="null"/>
    /// restores the default (full source window). The crop takes effect on
    /// the next <see cref="UpdateDestinationRect"/> call.
    /// </summary>
    /// <param name="cropInSourcePixels">
    /// Crop rectangle in source-window pixel coordinates, or
    /// <see langword="null"/> to clear.
    /// </param>
    public void SetSourceCrop(Rect? cropInSourcePixels)
    {
        _ui.AssertOnUiThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sourceCrop = cropInSourcePixels;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ui.AssertOnUiThread();
        if (_disposed)
        {
            s_logDoubleDispose(_log, Source, null);
            return;
        }

        _disposed = true;

        // Route the normal-path unregister through the INativeDwmApi seam so
        // the lifecycle test can observe exactly one Register/Unregister pair
        // per instance. We then mark the SafeHandle invalid so its own
        // finalizer-safety release (which goes straight to the P/Invoke, see
        // SafeDwmThumbnailHandle.ReleaseHandle) does NOT double-unregister.
        // If Dispose is skipped entirely, the SafeHandle finalizer still
        // releases as a last resort.
        var raw = Handle.DangerousGetHandle();
        try
        {
            _native.DwmUnregisterThumbnail(raw);
        }
        finally
        {
            Handle.SetHandleAsInvalid();
            Handle.Dispose();
        }
    }

    private static NativeMethods.RECT ToNative(Rect r) =>
        new()
        {
            Left = r.Left,
            Top = r.Top,
            Right = r.Right,
            Bottom = r.Bottom,
        };
}
