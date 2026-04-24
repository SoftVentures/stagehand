using System;
using System.Windows.Threading;

namespace App.Interop.Threading;

/// <summary>
/// Thin wrapper around the WPF <see cref="Dispatcher"/> that gives non-UI
/// layers (interop, services) a testable seam for posting work back to the
/// UI thread without taking a direct dependency on
/// <c>System.Windows.Application</c>. The single instance is created in the
/// shell composition root from <c>Application.Current.Dispatcher</c>.
/// </summary>
/// <remarks>
/// Lives in <c>App.Interop</c> (the lowest layer) so higher layers
/// (<c>App.Core</c>, <c>App.Services</c>, <c>App.Shell</c>) can all depend on
/// it. Plan 02 consumers: the real <c>WindowController</c> and
/// <c>WinEventHook</c> implementations post dispatcher continuations via
/// <see cref="Post"/> when marshalling raw Win32 callbacks to the UI thread.
/// </remarks>
/// <remarks>
/// Initializes a new <see cref="UiDispatcher"/> bound to the given
/// <paramref name="dispatcher"/>. The shell composition root passes
/// <c>Application.Current.Dispatcher</c>.
/// </remarks>
/// <param name="dispatcher">The WPF dispatcher of the UI thread.</param>
public sealed class UiDispatcher(Dispatcher dispatcher)
{
    private readonly Dispatcher _dispatcher =
        dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <summary>
    /// Posts <paramref name="action"/> asynchronously to the UI thread and
    /// returns immediately.
    /// </summary>
    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    /// <summary>
    /// Invokes <paramref name="action"/> synchronously on the UI thread.
    /// Blocks the caller until the delegate has run.
    /// </summary>
    public void Invoke(Action action) => _dispatcher.Invoke(action);

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the caller is not
    /// on the UI thread. Debug-only — elided from Release builds.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public void AssertOnUiThread()
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Call must run on the UI thread.");
        }
    }
}
