using System.Diagnostics.CodeAnalysis;

namespace App.Core.Stage;

/// <summary>
/// Placeholder <see cref="IStageController"/>. Every member throws
/// <see cref="NotImplementedException"/> until Plan 03 implements the Stage
/// orchestration layer.
/// </summary>
/// <remarks>
/// CA1065 flags property/event accessors that throw. This is intentional for a
/// stub whose sole purpose is to force any accidental wiring of the unimplemented
/// controller to fail loudly rather than silently no-op. The real implementation
/// in Plan 03 will not throw.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1065:Do not raise exceptions in unexpected locations",
    Justification = "Placeholder stub — intentionally throws from every member until Plan 03."
)]
public sealed class NotImplementedStageController : IStageController
{
    /// <inheritdoc />
    public StageState CurrentState => throw new NotImplementedException("Implemented in Plan 03.");

    /// <inheritdoc />
    public event EventHandler<StageState>? StateChanged
    {
        add => throw new NotImplementedException("Implemented in Plan 03.");
        remove => throw new NotImplementedException("Implemented in Plan 03.");
    }

    /// <inheritdoc />
    public Task EnableAsync(CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Plan 03.");

    /// <inheritdoc />
    public Task DisableAsync(CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Plan 03.");

    /// <inheritdoc />
    public Task SwapAsync(WindowIdentity toActivate, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Plan 03.");
}
