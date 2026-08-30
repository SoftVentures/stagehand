namespace App.Services.Snapshot;

/// <summary>
/// Read-side companion to <c>App.Core.Stage.ISnapshotStore</c>. Exposes
/// the JSON-DTO-aware <see cref="ReadIfStale"/> separately from the
/// write/delete contract so the App.Core interface does not need to leak
/// the <see cref="SnapshotFile"/> shape.
/// </summary>
/// <remarks>
/// Production registration: the same <c>SnapshotStore</c> instance is
/// resolved as both <c>ISnapshotStore</c> and <see cref="ISnapshotReader"/>
/// in DI; tests can substitute either side independently.
/// </remarks>
public interface ISnapshotReader
{
    /// <summary>
    /// Returns the parsed snapshot iff one exists AND was written by a
    /// process that is no longer alive (or whose PID has been recycled).
    /// Returns <see langword="null"/> when no snapshot exists, when the
    /// owning process is still running, or when the file is malformed.
    /// Never throws.
    /// </summary>
    SnapshotFile? ReadIfStale();
}
