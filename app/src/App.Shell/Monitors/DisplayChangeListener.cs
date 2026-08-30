using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using App.Core.Branding;
using App.Interop;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Shell.Monitors;

/// <summary>
/// Listens for <c>WM_DISPLAYCHANGE</c> notifications on a hidden message-only
/// window and raises <see cref="DisplayChanged"/> on the UI thread, debounced
/// to 200 ms to coalesce dock/undock storms.
/// </summary>
/// <remarks>Plan 03 §Design.8.</remarks>
public sealed class DisplayChangeListener : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(200);

    private static readonly Action<ILogger, Exception?> s_logFiring = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(11001, nameof(DisplayChangeListener) + ".Firing"),
        "WM_DISPLAYCHANGE coalesced; raising DisplayChanged."
    );

    private readonly UiDispatcher _ui;
    private readonly ILogger<DisplayChangeListener> _log;
    private readonly MessageOnlyWindow _window;
    private readonly DispatcherTimer _debounce;
    private bool _disposed;

    /// <summary>Fires (debounced) when the display topology changes.</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Production constructor — must run on the UI thread.</summary>
    public DisplayChangeListener(UiDispatcher ui, ILogger<DisplayChangeListener> log)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _ui.AssertOnUiThread();
        _window = new MessageOnlyWindow(BrandConstants.DisplayChangeWindowClassPrefix);
        _window.MessageReceived += OnMessage;
        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DebounceWindow,
        };
        _debounce.Tick += OnDebounceTick;
    }

    /// <summary>HWND of the underlying message-only window. Exposed for diagnostics.</summary>
    public IntPtr Hwnd => _window.Hwnd;

    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA1816:Dispose methods should call SuppressFinalize",
        Justification = "Sealed class with no finalizer."
    )]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;
        _window.MessageReceived -= OnMessage;
        _window.Dispose();
    }

    private void OnMessage(object? sender, MessageOnlyWindowMessageEventArgs e)
    {
        if (e.Message != NativeMethods.WM_DISPLAYCHANGE)
        {
            return;
        }
        // Restart the debounce timer; the OS often fires several
        // WM_DISPLAYCHANGE messages back-to-back during dock/undock.
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        s_logFiring(_log, null);
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
}
