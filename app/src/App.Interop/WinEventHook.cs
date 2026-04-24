using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using App.Interop.Internal;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Interop;

/// <summary>
/// Plan-02 WinEvent hook wrapper. Installs one <c>SetWinEventHook</c>
/// subscription on the shared <see cref="WinEventHookThread"/>, applies
/// per-instance coalescing, and marshals each delivered event back to the
/// UI thread via <see cref="UiDispatcher"/> before invoking
/// <see cref="Fired"/>.
/// </summary>
/// <remarks>
/// <para>
/// No matching interface: Plan 02 requires a richer surface (per-hook
/// coalescing, out-of-band Win32 callback marshalling, disposable unhook
/// semantics) than a minimal abstraction could express. Callers construct
/// this type directly or via <see cref="IWinEventHookFactory"/>.
/// </para>
/// <para>
/// <b>Coalescing scope is per instance.</b> Two hooks subscribed to
/// overlapping event ranges each keep their own dedupe ring and each
/// receive every matching event — the hook layer never silently dedupes
/// across subscribers.
/// </para>
/// <para>
/// <b>Explorer restart.</b> Plan 02 §Design.5 calls for re-installing
/// hooks when Explorer raises <c>TaskbarCreated</c>. This class leaves
/// that responsibility as a TODO for Plan 03 / Plan 05 (see
/// <c>docs/plans/02-core-window-mechanics.md</c> §Design.5). The plumbing
/// (installing a message-only window on the hook thread, registering for
/// <c>TaskbarCreated</c>, maintaining a global roster of live hooks to
/// reinstall) is invasive enough that folding it into this class now
/// would expand S5's scope materially without a current consumer.
/// </para>
/// </remarks>
public sealed class WinEventHook : IDisposable
{
    private readonly WinEventSpec _spec;
    private readonly UiDispatcher _ui;
    private readonly WinEventHookThread _thread;
    private readonly ILogger<WinEventHook> _log;
    private readonly INativeEventApi _native;

    // Keep the delegate alive for as long as the hook exists — the CLR must
    // not collect it while user32 still holds a function pointer to it.
    private readonly NativeMethods.WinEventDelegate _callback;

    // Per-instance coalescing ring. Key = (eventId, hwnd). Value = last
    // forwarded timestamp (UTC ticks) used to compare against CoalesceWindow.
    // ConcurrentDictionary because the WinEvent callback runs on the hook
    // thread while Dispose races from the caller's thread.
    private readonly ConcurrentDictionary<(uint EventId, IntPtr Hwnd), long> _lastForwardedTicks =
        new();

    private SafeWinEventHookHandle? _handle;
    private bool _disposed;

    private static readonly Action<ILogger, Exception?> s_logDisposeAfterShutdown =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(WinEventHook) + ".DisposeAfterShutdown"),
            "WinEventHook.Dispose could not post UnhookWinEvent because the hook thread is already stopped. The OS will clean up on process exit."
        );

    private static readonly Action<ILogger, Exception?> s_logSubscriberFault = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2, nameof(WinEventHook) + ".SubscriberFault"),
        "A WinEventHook.Fired subscriber threw an exception; continuing."
    );

    /// <summary>
    /// Creates a hook for the production code path: installs a real hook
    /// via <see cref="NativeEventApi"/>.
    /// </summary>
    public WinEventHook(
        WinEventSpec spec,
        UiDispatcher ui,
        WinEventHookThread thread,
        ILogger<WinEventHook> log
    )
        : this(spec, ui, thread, log, new NativeEventApi()) { }

    /// <summary>
    /// Test-friendly constructor allowing an alternative
    /// <see cref="INativeEventApi"/>. Visible to the Tests assembly via
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    internal WinEventHook(
        WinEventSpec spec,
        UiDispatcher ui,
        WinEventHookThread thread,
        ILogger<WinEventHook> log,
        INativeEventApi native
    )
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _thread = thread ?? throw new ArgumentNullException(nameof(thread));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _native = native ?? throw new ArgumentNullException(nameof(native));

        _callback = WinEventProc;

        // Construction from the hook thread itself would deadlock: the
        // constructor waits synchronously on InvokeAsync below, but the
        // dispatch that would drain that work item only runs on the same
        // thread. Assert loudly in Debug; callers have no legitimate reason
        // to new up a WinEventHook from inside a pump callback.
        Debug.Assert(
            !_thread.IsOnHookThread,
            "WinEventHook must not be constructed from the hook thread (would deadlock on InvokeAsync waiting for itself)."
        );

        // Install on the hook thread so callbacks land there (not on the
        // caller's thread). InvokeAsync blocks the caller until the hook
        // is installed — hook creation is synchronous from the caller's POV.
        Task installTask = _thread.InvokeAsync(() =>
        {
            _handle = _native.SetWinEventHook(
                _spec.EventMin,
                _spec.EventMax,
                _callback,
                _spec.IdProcess,
                _spec.IdThread,
                (uint)_spec.Flags
            );
        });

        // Bounded wait: if the hook thread is wedged we must not hang the
        // constructor indefinitely (the Plan-02 harness's "Register hook"
        // button froze the whole app here before the timeout was added).
        // A faulted install surfaces through `Wait` throwing AggregateException —
        // we unwrap to the underlying exception for a cleaner stack.
        try
        {
            if (!installTask.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "SetWinEventHook installation timed out after 5 seconds."
                );
            }
        }
        catch (AggregateException ae) when (ae.InnerException is not null)
        {
            throw ae.InnerException;
        }
    }

    /// <summary>
    /// Raised for every delivered WinEvent that passes the per-instance
    /// coalescing filter. Invoked on the UI thread (via
    /// <see cref="UiDispatcher.Post"/>), never on the hook thread.
    /// </summary>
    public event EventHandler<WinEventArgs>? Fired;

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Dispose must be idempotent and must never throw — log and continue."
    )]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        SafeWinEventHookHandle? handle = _handle;
        if (handle is null)
        {
            return;
        }

        try
        {
            // Post the unhook to the hook thread so SetWinEventHook /
            // UnhookWinEvent stay on the same thread (documented
            // requirement for WINEVENT_OUTOFCONTEXT hooks).
            Task task = _thread.InvokeAsync(handle.Dispose);
            // Don't block forever — if the thread is shutting down we'd
            // rather log and move on than deadlock Dispose.
            if (!task.Wait(TimeSpan.FromSeconds(1)))
            {
                // Hook thread didn't drain in time; fall through to the OS
                // cleanup on process exit.
            }
        }
        catch (ObjectDisposedException)
        {
            s_logDisposeAfterShutdown(_log, null);
        }
        catch (InvalidOperationException)
        {
            s_logDisposeAfterShutdown(_log, null);
        }
        catch (Exception ex)
        {
            // Any unexpected failure is still non-fatal; the OS cleans up
            // the underlying hook when the process exits.
            s_logDisposeAfterShutdown(_log, ex);
        }
    }

    // ---------------------------------------------------------------------
    // Callback path
    // ---------------------------------------------------------------------

    private void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime
    ) => OnRawEvent(eventType, hwnd, idObject, idChild, dwEventThread, dwmsEventTime);

    /// <summary>
    /// Test seam: drives the coalescing + marshalling path as if the OS
    /// had delivered <paramref name="eventType"/> for <paramref name="hwnd"/>.
    /// The Tests assembly uses this to verify behaviour without hooking
    /// real Win32 events.
    /// </summary>
    internal void InvokeCallbackForTesting(
        uint eventType,
        IntPtr hwnd,
        int idObject = 0,
        int idChild = 0,
        uint eventThread = 0,
        uint eventTimeMs = 0
    ) => OnRawEvent(eventType, hwnd, idObject, idChild, eventThread, eventTimeMs);

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Subscribers must not tear down the hook thread or UI thread; log and continue."
    )]
    private void OnRawEvent(
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTimeMs
    )
    {
        if (_disposed)
        {
            return;
        }

        if (_spec.CoalesceWindow is TimeSpan window)
        {
            var now = DateTime.UtcNow.Ticks;
            (uint eventType, nint hwnd) key = (eventType, hwnd);
            var windowTicks = window.Ticks;

            // Atomically read-or-add the last-forwarded timestamp. If the
            // previous value is within the coalesce window, drop the event
            // without publishing a new timestamp; otherwise update the
            // stamp to "now" and forward.
            var shouldForward = true;
            _lastForwardedTicks.AddOrUpdate(
                key,
                _ => now,
                (_, prev) =>
                {
                    if (now - prev < windowTicks)
                    {
                        shouldForward = false;
                        return prev;
                    }
                    return now;
                }
            );

            if (!shouldForward)
            {
                return;
            }
        }

        var args = new WinEventArgs(
            EventId: eventType,
            Hwnd: hwnd,
            ObjectId: idObject,
            ChildId: idChild,
            Thread: eventThread,
            Time: eventTimeMs
        );

        _ui.Post(() =>
        {
            EventHandler<WinEventArgs>? handler = Fired;
            if (handler is null)
            {
                return;
            }
            try
            {
                handler.Invoke(this, args);
            }
            catch (Exception ex)
            {
                s_logSubscriberFault(_log, ex);
            }
        });
    }
}
