using App.Interop.Internal;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Production implementation of <see cref="IWinEventHookFactory"/>.
/// Composes a <see cref="WinEventHook"/> from the shared
/// <see cref="UiDispatcher"/>, <see cref="WinEventHookThread"/>, and
/// native-event seam, producing one logger per hook from the supplied
/// <see cref="ILoggerFactory"/>.
/// </summary>
public sealed class WinEventHookFactory : IWinEventHookFactory
{
    private readonly UiDispatcher _ui;
    private readonly WinEventHookThread _thread;
    private readonly ILoggerFactory _loggers;
    private readonly INativeEventApi _native;

    /// <summary>
    /// Production constructor. The <see cref="INativeEventApi"/> defaults to
    /// the real <see cref="NativeEventApi"/>.
    /// </summary>
    public WinEventHookFactory(UiDispatcher ui, WinEventHookThread thread, ILoggerFactory loggers)
        : this(ui, thread, loggers, new NativeEventApi()) { }

    /// <summary>
    /// Test constructor allowing a substituted <see cref="INativeEventApi"/>.
    /// </summary>
    internal WinEventHookFactory(
        UiDispatcher ui,
        WinEventHookThread thread,
        ILoggerFactory loggers,
        INativeEventApi native
    )
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _thread = thread ?? throw new ArgumentNullException(nameof(thread));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    /// <inheritdoc />
    public WinEventHook Create(WinEventSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ILogger<WinEventHook> log = _loggers.CreateLogger<WinEventHook>();
        return new WinEventHook(spec, _ui, _thread, log, _native);
    }
}
