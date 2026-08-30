namespace App.Core.Stage;

/// <summary>
/// Executes a <see cref="SceneSwapPlan"/> — i.e. moves the underlying
/// real windows. Plan 03 ships the synchronous
/// <c>InstantSceneSwapExecutor</c>; Plan 04 swaps the DI registration to
/// the animated <c>SwapAnimationController</c> which shares this contract.
/// </summary>
/// <remarks>
/// Implementations own their own concurrency / queueing semantics. The
/// controller releases its swap semaphore <em>before</em> calling
/// <see cref="RunAsync"/> — see Plan 03 §Design.7 ("Why release the
/// semaphore before the animation").
/// </remarks>
public interface ISceneSwapExecutor
{
    /// <summary>
    /// Performs the moves described in <paramref name="plan"/> and brings
    /// <see cref="SceneSwapPlan.IncomingPrimary"/> to the front.
    /// </summary>
    Task RunAsync(SceneSwapPlan plan, AnimationSpeed speed, CancellationToken ct);
}
