namespace App.Services.Snapshot;

/// <summary>
/// Resolves the absolute path to the Stage snapshot file. Plan 03 §Design.9
/// chose <c>%LOCALAPPDATA%\Stagehand\state\snapshot.json</c>.
/// </summary>
public interface ISnapshotPathProvider
{
    /// <summary>Absolute path to <c>snapshot.json</c>.</summary>
    string SnapshotFilePath { get; }
}
