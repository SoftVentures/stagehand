namespace App.Core.Stage;

/// <summary>
/// The top-level orchestrator for the Stage subsystem. Drives transitions
/// between <see cref="StagePhase"/> values and owns the authoritative
/// <see cref="StageState"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading contract.</b> All public mutation methods
/// (<see cref="EnableAsync"/>, <see cref="DisableAsync"/>,
/// <see cref="SwapAsync"/>, <see cref="SwapByWindowAsync"/>,
/// <see cref="MoveWindowToSceneAsync"/>) are safe to call from any
/// thread; the implementation serialises them internally via semaphores.
/// They follow the async/await convention and may continue on a thread
/// pool worker after the first <c>await</c>.
/// </para>
/// <para>
/// <b><see cref="StateChanged"/> reentrancy.</b> The event fires
/// <em>after</em> the controller has released its internal semaphore,
/// so a subscriber may call back into any of the public mutation
/// methods without deadlocking. Subscribers MUST nevertheless avoid
/// blocking the event handler synchronously: a long-running handler
/// stalls the post-mutation drain. Marshal back to the UI thread or
/// to a worker as appropriate.
/// </para>
/// </remarks>
public interface IStageController
{
    /// <summary>Current Stage state snapshot.</summary>
    StageState CurrentState { get; }

    /// <summary>Convenience: <c>CurrentState.Phase == Enabled</c>.</summary>
    bool IsEnabled { get; }

    /// <summary>Raised after the state transitions to a new value.</summary>
    /// <remarks>
    /// CA1003 suggests an <see cref="EventArgs"/>-derived payload. <see cref="StageState"/>
    /// is an immutable record snapshot used deliberately so subscribers can diff/compare
    /// states by value without allocating a wrapper event-args type per transition.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "StageState is the immutable snapshot payload; an EventArgs wrapper adds no value."
    )]
    event EventHandler<StageState>? StateChanged;

    /// <summary>Transition from <see cref="StagePhase.Disabled"/> to <see cref="StagePhase.Enabled"/>.</summary>
    Task EnableAsync(CancellationToken ct);

    /// <summary>Transition from <see cref="StagePhase.Enabled"/> to <see cref="StagePhase.Disabled"/>.</summary>
    Task DisableAsync(CancellationToken ct);

    /// <summary>
    /// Swap the active scene on <paramref name="deviceName"/> with the parked
    /// scene identified by <paramref name="target"/>. No-op when the target
    /// is already active or the controller is not <see cref="StagePhase.Enabled"/>.
    /// </summary>
    Task SwapAsync(SceneId target, string deviceName, CancellationToken ct);

    /// <summary>
    /// Resolve <paramref name="foregroundWindow"/> to its scene (across all
    /// monitors) and swap that scene to the foreground on its monitor.
    /// Used by the foreground-window WinEvent hook to honour Alt-Tab /
    /// taskbar clicks.
    /// </summary>
    Task SwapByWindowAsync(WindowIdentity foregroundWindow, CancellationToken ct);

    /// <summary>
    /// Move <paramref name="window"/> from its current scene to
    /// <paramref name="targetScene"/>. When <paramref name="targetScene"/>
    /// is <see langword="null"/> a brand-new scene is minted on the
    /// window's current monitor with the window as its sole member and
    /// primary.
    /// </summary>
    Task MoveWindowToSceneAsync(WindowIdentity window, SceneId? targetScene, CancellationToken ct);

    /// <summary>
    /// Re-enumerates the desktop, picks up any manageable windows the user
    /// opened after <see cref="EnableAsync"/>, groups them into scenes
    /// (joining same-process scenes when grouping mode = ByProcessId),
    /// parks the new HWNDs, and re-syncs the affected monitor overlays.
    /// Existing scene members keep their stored <c>OriginalBounds</c>.
    /// No-op when the controller is not <see cref="StagePhase.Enabled"/>.
    /// </summary>
    Task IngestNewWindowsAsync(CancellationToken ct);
}
