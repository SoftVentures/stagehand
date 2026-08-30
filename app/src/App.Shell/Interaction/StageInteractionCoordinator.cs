using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Shell.Interaction;

/// <summary>
/// Wires user-driven interaction sources (sidebar tile clicks, foreground
/// window changes via WinEvent hooks) to the corresponding
/// <see cref="IStageController"/> mutation calls. Owns no persistent state
/// beyond hook handles.
/// </summary>
/// <remarks>
/// Plan 03 §S5. <see cref="Start"/> on app startup; <see cref="Stop"/> on
/// shutdown.
/// </remarks>
public sealed class StageInteractionCoordinator : IDisposable
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logForegroundIgnored =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Trace,
            new EventId(9001, nameof(StageInteractionCoordinator) + ".ForegroundIgnored"),
            "Foreground change for HWND 0x{Hwnd:X} ignored (not in any parked scene)."
        );

    private static readonly Action<ILogger, Exception?> s_logSwapFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(9002, nameof(StageInteractionCoordinator) + ".SwapFailed"),
        "Swap triggered by user interaction failed."
    );

    private static readonly Action<ILogger, Exception?> s_logHookInstallFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(9003, nameof(StageInteractionCoordinator) + ".HookInstallFailed"),
            "Foreground WinEvent hook install failed; Alt-Tab/foreground swaps will not be available until next restart."
        );

    private readonly IStageController _stage;
    private readonly IStageOverlayHost _overlay;
    private readonly IWinEventHookFactory _hooks;
    private readonly UiDispatcher _ui;
    private readonly ILogger<StageInteractionCoordinator> _log;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by Stop() which Dispose() always calls."
    )]
    private WinEventHook? _foregroundHook;
    private bool _started;
    private bool _disposed;

    /// <summary>Production constructor.</summary>
    public StageInteractionCoordinator(
        IStageController stage,
        IStageOverlayHost overlay,
        IWinEventHookFactory hooks,
        UiDispatcher ui,
        ILogger<StageInteractionCoordinator> log
    )
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Subscribes to interaction sources.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failed hook install must not prevent the click path from working — log and continue."
    )]
    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        _overlay.SceneClicked += OnSceneClicked;

        try
        {
            _foregroundHook = _hooks.Create(
                new WinEventSpec(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND
                )
            );
            _foregroundHook.Fired += OnForegroundChanged;
        }
        catch (Exception ex)
        {
            s_logHookInstallFailed(_log, ex);
        }
    }

    /// <summary>Unsubscribes and tears down hooks.</summary>
    public void Stop()
    {
        if (!_started)
        {
            return;
        }
        _started = false;

        _overlay.SceneClicked -= OnSceneClicked;

        if (_foregroundHook is { } h)
        {
            h.Fired -= OnForegroundChanged;
            h.Dispose();
            _foregroundHook = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
    }

    [SuppressMessage(
        "Reliability",
        "CA2008:Do not create tasks without passing a TaskScheduler",
        Justification = "Fire-and-forget swap initiated by a user click; the IStageController itself enforces semaphore semantics."
    )]
    private void OnSceneClicked(object? sender, SceneClickedEventArgs e)
    {
        _ = SwapScopeAsync(() => _stage.SwapAsync(e.Scene, e.DeviceName, CancellationToken.None));
    }

    [SuppressMessage(
        "Reliability",
        "CA2008:Do not create tasks without passing a TaskScheduler",
        Justification = "Fire-and-forget swap initiated by a foreground change; the IStageController itself enforces semaphore semantics."
    )]
    private void OnForegroundChanged(object? sender, WinEventArgs e)
    {
        // Try to resolve against the existing parked-scene set first.
        WindowIdentity? identity = ResolveIdentityFromForegroundHwnd(e.Hwnd);
        if (identity is not null)
        {
            _ = SwapScopeAsync(() =>
                _stage.SwapByWindowAsync(identity.Value, CancellationToken.None)
            );
            return;
        }

        // Unknown HWND: it might be a window the user just opened after
        // Enable. Ingest the desktop, then re-resolve. If still unknown
        // (system tray helper, a window we filter out, the active scene's
        // own window, etc.) it's a no-op.
        IntPtr capturedHwnd = e.Hwnd;
        _ = SwapScopeAsync(async () =>
        {
            await _stage.IngestNewWindowsAsync(CancellationToken.None).ConfigureAwait(false);
            WindowIdentity? after = ResolveIdentityFromForegroundHwnd(capturedHwnd);
            if (after is not null)
            {
                await _stage
                    .SwapByWindowAsync(after.Value, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                s_logForegroundIgnored(_log, capturedHwnd, null);
            }
        });
    }

    private WindowIdentity? ResolveIdentityFromForegroundHwnd(IntPtr hwnd)
    {
        StageState s = _stage.CurrentState;
        foreach (KeyValuePair<string, ImmutableList<Scene>> pair in s.ScenesByDevice)
        {
            // Skip scenes that are already active on their monitor — switching
            // focus to one of their windows is a no-op (the scene is up).
            SceneId? activeId = s.ActiveSceneByDevice.TryGetValue(pair.Key, out SceneId? a)
                ? a
                : null;
            foreach (Scene scene in pair.Value)
            {
                if (scene.Id == activeId)
                {
                    continue;
                }
                foreach (ParkedWindow pw in scene.Windows)
                {
                    if (pw.Identity.Hwnd == hwnd)
                    {
                        return pw.Identity;
                    }
                }
            }
        }
        return null;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Interaction-driven swaps are best-effort; a thrown exception must not crash the dispatcher."
    )]
    private async Task SwapScopeAsync(Func<Task> swap)
    {
        try
        {
            await swap().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logSwapFailed(_log, ex);
        }
    }
}
