namespace App.Core.Stage;

/// <summary>
/// The top-level orchestrator for the Stage subsystem. Drives transitions
/// between <see cref="StagePhase"/> values and owns the authoritative
/// <see cref="StageState"/>.
/// </summary>
public interface IStageController
{
    /// <summary>Current Stage state snapshot.</summary>
    StageState CurrentState { get; }

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

    /// <summary>Bring the identified window to the front of the Stage.</summary>
    Task SwapAsync(WindowIdentity toActivate, CancellationToken ct);
}
