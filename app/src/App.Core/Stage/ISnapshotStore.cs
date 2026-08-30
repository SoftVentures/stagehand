namespace App.Core.Stage;

/// <summary>
/// Persists Stage snapshots to <c>%LOCALAPPDATA%\Stagehand\state\snapshot.json</c>
/// for crash recovery. Plan 03 §Design.9 owns the on-disk schema.
/// </summary>
/// <remarks>
/// The interface lives in <c>App.Core</c> so <c>StageController</c> can
/// depend on it; the implementation in <c>App.Services.Snapshot.SnapshotStore</c>
/// (Plan 03 §S7) translates between <see cref="StageState"/> and the JSON
/// schema.
/// </remarks>
public interface ISnapshotStore
{
    /// <summary>Whether a snapshot file is currently present on disk.</summary>
    bool Exists { get; }

    /// <summary>
    /// Atomically writes a snapshot derived from <paramref name="state"/>.
    /// Replaces any existing snapshot.
    /// </summary>
    Task WriteAsync(StageState state, CancellationToken ct);

    /// <summary>Deletes the snapshot file if present; no-op when absent.</summary>
    Task DeleteAsync(CancellationToken ct);
}
