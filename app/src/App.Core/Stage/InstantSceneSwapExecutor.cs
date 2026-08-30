using App.Interop;
using Microsoft.Extensions.Logging;

namespace App.Core.Stage;

/// <summary>
/// Synchronous, no-animation executor: walks <see cref="SceneSwapPlan.OutgoingMoves"/>
/// and <see cref="SceneSwapPlan.IncomingMoves"/> calling
/// <see cref="IWindowController.Resize"/> for each, then
/// <see cref="IWindowController.BringToFront"/> on the incoming primary.
/// </summary>
/// <remarks>
/// Plan 03 §Design.5a. Plan 04 replaces the DI registration with an
/// animated executor that shares <see cref="ISceneSwapExecutor"/>.
/// </remarks>
public sealed class InstantSceneSwapExecutor : ISceneSwapExecutor
{
    private static readonly Action<ILogger, string, Exception?> s_logSwapStart =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(InstantSceneSwapExecutor) + ".Start"),
            "InstantSceneSwapExecutor.RunAsync starting on monitor '{Device}'."
        );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logResizeFailed =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(2, nameof(InstantSceneSwapExecutor) + ".ResizeFailed"),
            "InstantSceneSwapExecutor: Resize for HWND 0x{Hwnd:X} failed; continuing."
        );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logBringToFrontFailed =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Information,
            new EventId(3, nameof(InstantSceneSwapExecutor) + ".BringToFrontFailed"),
            "InstantSceneSwapExecutor: BringToFront(0x{Hwnd:X}) was denied; the user can retry by clicking the window."
        );

    private readonly IWindowController _windows;
    private readonly ILogger<InstantSceneSwapExecutor> _log;

    /// <summary>Production constructor.</summary>
    public InstantSceneSwapExecutor(
        IWindowController windows,
        ILogger<InstantSceneSwapExecutor> log
    )
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public Task RunAsync(SceneSwapPlan plan, AnimationSpeed speed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // The 'speed' argument is intentionally ignored — the instant
        // executor has no animation budget. The signature matches the
        // animated executor in Plan 04 so the DI registration can swap.
        _ = speed;

        s_logSwapStart(_log, plan.TargetDeviceName, null);

        // Order matters: parking the outgoing scene FIRST creates a
        // foreground-vacuum (the active window slides off-screen, Windows
        // auto-picks the next Z-order HWND as foreground). The
        // StageInteractionCoordinator's foreground hook then sees an unrelated
        // parked window and fires another swap → infinite swap loop.
        //
        // Move the incoming scene + bring it to the front FIRST so a real
        // foreground exists before any outgoing window vanishes.
        //
        // 1. Incoming scene → main area.
        foreach (SceneWindowMove move in plan.IncomingMoves)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _windows.Resize(move.Identity.Hwnd, move.To);
            }
            catch (App.Interop.Errors.Win32InteropException)
            {
                s_logResizeFailed(_log, move.Identity.Hwnd, null);
            }
            catch (App.Interop.Errors.ElevationBoundaryException)
            {
                s_logResizeFailed(_log, move.Identity.Hwnd, null);
            }
        }

        // 2. Bring incoming primary to the front (best effort) — claims the
        //    foreground slot before the outgoing windows release it.
        try
        {
            _windows.BringToFront(plan.IncomingPrimary.Hwnd);
        }
        catch (App.Interop.Errors.Win32InteropException)
        {
            s_logBringToFrontFailed(_log, plan.IncomingPrimary.Hwnd, null);
        }

        // 3. Outgoing scene → sidebar park slots. Foreground is already
        //    on the incoming primary, so parking these does not create a
        //    foreground-vacuum that would race the WinEvent hook.
        foreach (SceneWindowMove move in plan.OutgoingMoves)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _windows.Resize(move.Identity.Hwnd, move.To);
            }
            catch (App.Interop.Errors.Win32InteropException)
            {
                s_logResizeFailed(_log, move.Identity.Hwnd, null);
            }
            catch (App.Interop.Errors.ElevationBoundaryException)
            {
                s_logResizeFailed(_log, move.Identity.Hwnd, null);
            }
        }

        return Task.CompletedTask;
    }
}
