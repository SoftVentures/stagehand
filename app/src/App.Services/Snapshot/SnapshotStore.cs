using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using App.Core.Stage;
using App.Core.Time;
using Microsoft.Extensions.Logging;

namespace App.Services.Snapshot;

/// <summary>
/// Production <see cref="ISnapshotStore"/>. Atomic writes via temp + rename;
/// stale-detection compares the persisted PID and process start time against
/// the live process table. Plan 03 §Design.9 + §S6.
/// </summary>
public sealed class SnapshotStore : ISnapshotStore, ISnapshotReader
{
    /// <summary>
    /// Tolerance applied when comparing the persisted process start time
    /// against <see cref="Process.StartTime"/>. <c>Process.StartTime</c>
    /// has ~16 ms granularity on Windows (one timer tick) and round-tripping
    /// through JSON / FILETIME conversions can add a small amount of
    /// further drift. 20 ms (200000 ticks) is loose enough that the same
    /// process's recorded vs. live start times match reliably, tight
    /// enough that a recycled PID — which would have a start time
    /// orders of magnitude further out — is still recognised as stale.
    /// </summary>
    private const long StartTimeToleranceTicks = 200_000L;

    private static readonly Action<ILogger, string, Exception?> s_logRead =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(13001, nameof(SnapshotStore) + ".Read"),
            "SnapshotStore: stale snapshot detected at '{Path}'."
        );

    private static readonly Action<ILogger, string, Exception?> s_logReadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(13002, nameof(SnapshotStore) + ".ReadFailed"),
            "SnapshotStore: failed to read snapshot at '{Path}'; treating as absent."
        );

    private static readonly Action<ILogger, int, Exception?> s_logUnexpectedSchema =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(13003, nameof(SnapshotStore) + ".UnexpectedSchema"),
            "SnapshotStore: snapshot has schemaVersion={Version}; this build was tested with version 1. Restoring on a best-effort basis."
        );

    /// <summary>
    /// The snapshot schema version this build writes. Plan 03 owns v1.
    /// Bump when the on-disk JSON shape changes incompatibly.
    /// </summary>
    private const int CurrentSchemaVersion = 1;

    private readonly ISnapshotPathProvider _paths;
    private readonly IClock _clock;
    private readonly ILogger<SnapshotStore> _log;

    /// <summary>Production constructor.</summary>
    public SnapshotStore(ISnapshotPathProvider paths, IClock clock, ILogger<SnapshotStore> log)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public bool Exists => File.Exists(_paths.SnapshotFilePath);

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort .tmp cleanup must never mask the original failure — swallow and rethrow."
    )]
    public async Task WriteAsync(StageState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);

        SnapshotFile file = ToFile(state, _clock.UtcNow);
        var json = JsonSerializer.Serialize(file, JsonOptions);

        var path = _paths.SnapshotFilePath;
        var tempPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            // Atomic-write contract: if the rename failed, the .tmp file is
            // leftover noise — best-effort delete so a follow-up Write or
            // crash recovery doesn't see a stale partial. The original
            // exception propagates after the cleanup attempt.
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // intentional: don't mask the original failure
            }
            throw;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(CancellationToken ct)
    {
        if (File.Exists(_paths.SnapshotFilePath))
        {
            File.Delete(_paths.SnapshotFilePath);
        }
        // Belt-and-braces: if a previous Write failed mid-rename and left
        // a .tmp behind, the Delete contract should clean both.
        var tempPath = _paths.SnapshotFilePath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the snapshot if and only if the persisted PID is no longer a
    /// running process (or the persisted process start time disagrees with
    /// the running process by more than 20 ms — a recycled PID). Returns
    /// <see langword="null"/> when the snapshot is fresh, missing, or
    /// malformed.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Snapshot read happens at startup; any failure (malformed JSON, IO race) must be downgraded to 'no snapshot' rather than throwing."
    )]
    public SnapshotFile? ReadIfStale()
    {
        var path = _paths.SnapshotFilePath;
        if (!File.Exists(path))
        {
            return null;
        }
        SnapshotFile? file;
        try
        {
            var json = File.ReadAllText(path);
            file = JsonSerializer.Deserialize<SnapshotFile>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            s_logReadFailed(_log, path, ex);
            return null;
        }
        if (file is null)
        {
            return null;
        }
        if (file.SchemaVersion != CurrentSchemaVersion)
        {
            s_logUnexpectedSchema(_log, file.SchemaVersion, null);
        }

        // PID look-up.
        try
        {
            using Process p = Process.GetProcessById(file.ProcessId);
            try
            {
                var liveTicks = p.StartTime.ToUniversalTime().Ticks;
                if (Math.Abs(liveTicks - file.ProcessStartTimeUtcTicks) <= StartTimeToleranceTicks)
                {
                    // Same process still running — snapshot is fresh.
                    return null;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Documented failure for Process.StartTime on system PIDs
                // (4 — System Idle, 8 — System) and any process Stagehand
                // can't inspect at our integrity level — treat as stale.
            }
            catch (UnauthorizedAccessException)
            {
                // Cross-session or higher-integrity process — treat as stale.
            }
            catch (InvalidOperationException)
            {
                // Process exited between GetProcessById and StartTime read.
            }
            catch (NotSupportedException)
            {
                // Documented for processes outside the current machine.
            }
        }
        catch (ArgumentException)
        {
            // Process not found — definitely stale.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // GetProcessById failed before construction (rare, e.g. snapshot.json
            // on a different OS volume returns negative PID).
        }
        catch (InvalidOperationException)
        {
            // Process exited between PID read and handle construction.
        }

        s_logRead(_log, path, null);
        return file;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static SnapshotFile ToFile(StageState state, DateTimeOffset createdAt)
    {
        var current = Process.GetCurrentProcess();
        var file = new SnapshotFile
        {
            SchemaVersion = CurrentSchemaVersion,
            CreatedAtUtc = createdAt,
            ProcessId = current.Id,
            ProcessStartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks,
        };

        foreach (KeyValuePair<string, SavedWorkArea> pair in state.SavedWorkAreasByDevice)
        {
            SceneId? active = state.ActiveSceneByDevice.TryGetValue(pair.Key, out SceneId? a)
                ? a
                : null;
            file.Monitors.Add(
                new SnapshotMonitor
                {
                    DeviceName = pair.Key,
                    SavedWorkArea = ToRect(pair.Value.OriginalWorkArea),
                    ActiveSceneId = active?.Value,
                }
            );
        }

        foreach (KeyValuePair<string, ImmutableList<Scene>> pair in state.ScenesByDevice)
        {
            foreach (Scene scene in pair.Value)
            {
                var snapshotScene = new SnapshotScene
                {
                    Id = scene.Id.Value,
                    MonitorDeviceName = pair.Key,
                    Title = scene.Title,
                    Primary = ToIdentity(scene.Primary),
                    CreatedAtUtc = scene.CreatedAt,
                };
                foreach (ParkedWindow pw in scene.Windows)
                {
                    snapshotScene.Windows.Add(
                        new SnapshotWindow
                        {
                            Identity = ToIdentity(pw.Identity),
                            OriginalBounds = ToRect(pw.OriginalBounds),
                            OriginalMonitor = pw.OriginalMonitorDeviceName,
                            IsElevated = pw.IsElevated,
                        }
                    );
                }
                file.Scenes.Add(snapshotScene);
            }
        }

        return file;
    }

    private static SnapshotRect ToRect(App.Interop.Rect r) =>
        new()
        {
            X = r.X,
            Y = r.Y,
            W = r.Width,
            H = r.Height,
        };

    private static SnapshotIdentity ToIdentity(WindowIdentity id) =>
        new()
        {
            Hwnd = id.Hwnd.ToInt64(),
            ProcessId = id.ProcessId,
            ProcessStartTimeUtcTicks = id.ProcessStartTimeUtcTicks,
        };
}
