using App.Interop.Internal;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Production <see cref="IDwmThumbnailFactory"/>. Calls
/// <c>DwmRegisterThumbnail</c> through the <see cref="INativeDwmApi"/>
/// seam and hands the resulting <see cref="SafeDwmThumbnailHandle"/> to a
/// new <see cref="DwmThumbnail"/> wrapper.
/// </summary>
/// <remarks>
/// The factory itself holds no state — it is a singleton in the DI
/// container. The <see cref="ILoggerFactory"/> is used to mint a per-
/// thumbnail <see cref="ILogger{TCategoryName}"/> so Trace-level log
/// messages (e.g. double-dispose) carry the correct category.
/// </remarks>
public sealed class DwmThumbnailFactory : IDwmThumbnailFactory
{
    private readonly INativeDwmApi _native;
    private readonly UiDispatcher _ui;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Production constructor. Constructs the default
    /// <see cref="NativeDwmApi"/> seam internally. Called from the
    /// <c>ServiceConfiguration</c> composition root in <c>App.Shell</c>.
    /// </summary>
    public DwmThumbnailFactory(UiDispatcher ui, ILoggerFactory loggerFactory)
        : this(new NativeDwmApi(), ui, loggerFactory) { }

    /// <summary>
    /// Test-facing constructor. Injects an explicit
    /// <see cref="INativeDwmApi"/> so the lifecycle test can observe
    /// register / unregister pairs without DWM.
    /// </summary>
    internal DwmThumbnailFactory(
        INativeDwmApi native,
        UiDispatcher ui,
        ILoggerFactory loggerFactory
    )
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public DwmThumbnail Register(IntPtr sourceHwnd, IntPtr destinationHwnd)
    {
        _ui.AssertOnUiThread();
        SafeDwmThumbnailHandle handle = _native.DwmRegisterThumbnail(destinationHwnd, sourceHwnd);
        return new DwmThumbnail(
            handle,
            sourceHwnd,
            destinationHwnd,
            _native,
            _ui,
            _loggerFactory.CreateLogger<DwmThumbnail>()
        );
    }
}
