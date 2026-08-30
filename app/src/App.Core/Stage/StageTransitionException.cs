namespace App.Core.Stage;

/// <summary>
/// Thrown by <c>StageController.EnableAsync</c> when one of its lifecycle
/// steps fails irrecoverably. The wrapped <see cref="Exception.InnerException"/>
/// is the underlying cause; <see cref="From"/> and <see cref="To"/> describe
/// the transition that failed.
/// </summary>
public sealed class StageTransitionException : Exception
{
    /// <summary>Phase the controller was in when the transition began.</summary>
    public StagePhase From { get; }

    /// <summary>Phase the controller was attempting to reach.</summary>
    public StagePhase To { get; }

    /// <summary>Default constructor (kept for analyser compliance; production code uses the rich constructor).</summary>
    public StageTransitionException()
        : base("Stage transition failed.") { }

    /// <summary>Message-only constructor (kept for analyser compliance).</summary>
    public StageTransitionException(string message)
        : base(message) { }

    /// <summary>Message + inner exception constructor (kept for analyser compliance).</summary>
    public StageTransitionException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>The constructor production code uses.</summary>
    public StageTransitionException(StagePhase from, StagePhase to, Exception innerException)
        : base(
            $"Stage transition from {from} to {to} failed: {innerException?.Message ?? "no inner exception"}.",
            innerException
        )
    {
        From = from;
        To = to;
    }
}
