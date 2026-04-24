using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace App.Interop.Threading;

/// <summary>
/// Owns a dedicated STA thread that runs a manual Win32 message pump, used
/// to host <c>SetWinEventHook</c> callbacks off the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// <c>SetWinEventHook</c> with <c>WINEVENT_OUTOFCONTEXT</c> delivers
/// callbacks on the thread that registered the hook, via normal window
/// messages. Hosting the hook on the UI thread means a poorly-behaved
/// event source (e.g. <c>EVENT_OBJECT_LOCATIONCHANGE</c> firing thousands of
/// times per second during a window drag) could starve rendering. This
/// class provides a dedicated message-pump thread instead; the wrapper
/// <see cref="WinEventHook"/> marshals each event back to the UI thread
/// via <see cref="UiDispatcher"/>.
/// </para>
/// <para>
/// Lives in <c>App.Interop</c> (not <c>App.Shell</c>) on purpose — the
/// thread is pure Win32 infrastructure (STA + <c>GetMessage</c> loop) and
/// has no WPF dependency. Hosting it lower in the layer stack keeps the
/// hook/factory types in <c>App.Interop</c> without pulling a WPF
/// assembly upward.
/// </para>
/// <para>
/// Lifetime: the thread is lazily started on the first
/// <see cref="InvokeAsync"/> call and torn down by <see cref="Dispose"/>.
/// After <see cref="Dispose"/> any further <see cref="InvokeAsync"/> call
/// throws <see cref="ObjectDisposedException"/> — the hook wrappers catch
/// this during their own disposal and downgrade to a warning log.
/// </para>
/// </remarks>
/// <remarks>
/// Creates a new, non-started hook thread. The thread does not spin up
/// until the first <see cref="InvokeAsync"/> call.
/// </remarks>
/// <param name="log">Logger for lifecycle and fault events.</param>
[SuppressMessage(
    "Design",
    "CA1063:Implement IDisposable correctly",
    Justification = "Sealed class with a single Dispose implementation is sufficient; the full pattern with a finalizer is not needed because no unmanaged resources are held directly (the thread itself is a managed resource)."
)]
public sealed class WinEventHookThread(ILogger<WinEventHookThread> log) : IDisposable
{
    private readonly ILogger<WinEventHookThread> _log =
        log ?? throw new ArgumentNullException(nameof(log));
    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly object _startGate = new();
    private readonly ManualResetEventSlim _threadReady = new(false);

    private Thread? _thread;
    private uint _threadId;
    private bool _disposed;

    /// <summary>
    /// True when the caller's thread is the pump thread owned by this
    /// instance. Used by <see cref="WinEventHook"/> to assert in Debug that
    /// it is not being constructed from the hook thread (which would
    /// deadlock on <see cref="InvokeAsync"/> waiting for itself).
    /// </summary>
    public bool IsOnHookThread => _thread is { } t && Thread.CurrentThread == t;

    private static readonly Action<ILogger, Exception?> s_logThreadStarted = LoggerMessage.Define(
        LogLevel.Debug,
        new EventId(1, nameof(WinEventHookThread) + ".ThreadStarted"),
        "WinEvent hook thread started (pumping messages)."
    );

    private static readonly Action<ILogger, Exception?> s_logThreadExiting = LoggerMessage.Define(
        LogLevel.Debug,
        new EventId(2, nameof(WinEventHookThread) + ".ThreadExiting"),
        "WinEvent hook thread received WM_QUIT, exiting pump."
    );

    private static readonly Action<ILogger, Exception?> s_logThreadFault = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3, nameof(WinEventHookThread) + ".ThreadFault"),
        "WinEvent hook thread terminated with an unhandled exception."
    );

    private static readonly Action<ILogger, Exception?> s_logWorkItemFault = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(4, nameof(WinEventHookThread) + ".WorkItemFault"),
        "WinEvent hook thread work item threw; exception surfaces via the caller's Task."
    );

    /// <summary>
    /// Schedules <paramref name="action"/> to run on the hook thread and
    /// returns a <see cref="Task"/> that completes when the action finishes
    /// (or faults with whatever the action threw).
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// The thread has been stopped by <see cref="Dispose"/>.
    /// </exception>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureStarted();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new WorkItem(action, tcs));
        WakeUpPump();
        return tcs.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_startGate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        Thread? thread = _thread;
        var tid = _threadId;
        if (thread is null || tid == 0)
        {
            // Never started — nothing to tear down.
            _threadReady.Dispose();
            return;
        }

        // Ask the pump to exit.
        _ = NativeMethods.PostThreadMessage(tid, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        // Wait (briefly) for a clean exit; if the thread is wedged we don't
        // want Dispose to deadlock the caller, so cap the join.
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            // Thread didn't exit in time — leave it; process exit will reap.
        }

        // Fail any remaining queued work so awaiters don't hang.
        while (_queue.TryDequeue(out WorkItem leftover))
        {
            leftover.Completion.TrySetException(
                new ObjectDisposedException(nameof(WinEventHookThread))
            );
        }

        _threadReady.Dispose();
    }

    // ---------------------------------------------------------------------
    // Internal lifecycle
    // ---------------------------------------------------------------------

    private void EnsureStarted()
    {
        lock (_startGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                return;
            }

            var t = new Thread(PumpThreadEntry)
            {
                IsBackground = true,
                // Brand-neutral, still informative in a debugger. App.Interop
                // must not reference App.Core's BrandConstants (layer rule).
                Name = "WinEventHook pump",
            };
            t.SetApartmentState(ApartmentState.STA);
            _thread = t;
            t.Start();
        }

        // Wait for the pump to publish its thread id so PostThreadMessage
        // has somewhere to deliver wake-ups.
        _threadReady.Wait();
    }

    private void WakeUpPump()
    {
        var tid = _threadId;
        if (tid == 0)
        {
            return;
        }
        _ = NativeMethods.PostThreadMessage(tid, NativeMethods.WM_USER, IntPtr.Zero, IntPtr.Zero);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The pump loop must never tear down the process — log and exit gracefully."
    )]
    private void PumpThreadEntry()
    {
        try
        {
            // Force the thread to have a message queue (required before any
            // other thread is allowed to PostThreadMessage to it). Any user
            // message works; PeekMessage is the idiomatic form but GetMessage
            // works equally well once the queue exists.
            _threadId = NativeMethods.GetCurrentThreadId();
            _threadReady.Set();

            s_logThreadStarted(_log, null);

            while (true)
            {
                var result = NativeMethods.GetMessage(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0);
                if (result == 0)
                {
                    // WM_QUIT received.
                    break;
                }
                if (result == -1)
                {
                    // GetMessage failed; drain and exit to avoid a tight loop.
                    break;
                }

                if (msg.message == NativeMethods.WM_USER)
                {
                    DrainQueue();
                }
                else
                {
                    _ = NativeMethods.TranslateMessage(in msg);
                    _ = NativeMethods.DispatchMessage(in msg);
                }
            }

            // Drain one last time so callers awaiting the final InvokeAsync
            // don't hang if the WM_QUIT raced their post.
            DrainQueue();
            s_logThreadExiting(_log, null);
        }
        catch (Exception ex)
        {
            s_logThreadFault(_log, ex);
        }
    }

    private void DrainQueue()
    {
        while (_queue.TryDequeue(out WorkItem item))
        {
            try
            {
                item.Action();
                item.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                s_logWorkItemFault(_log, ex);
                item.Completion.TrySetException(ex);
            }
        }
    }

    private readonly record struct WorkItem(Action Action, TaskCompletionSource Completion);
}
