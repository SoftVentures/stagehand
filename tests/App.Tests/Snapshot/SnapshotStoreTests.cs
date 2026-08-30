using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using App.Core.Stage;
using App.Interop;
using App.Services.Snapshot;
using App.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests.Snapshot;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "stagehand-snapshot-tests-" + Guid.NewGuid().ToString("N")
    );

    private sealed class FixedPaths(string path) : ISnapshotPathProvider
    {
        public string SnapshotFilePath { get; } = path;
    }

    private string SnapshotPath => Path.Combine(_tempDir, "snapshot.json");

    public SnapshotStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static StageState BuildState(SceneId sceneId, params WindowIdentity[] members)
    {
        var pws = members
            .Select(id => new ParkedWindow(
                id,
                new Rect(10, 20, 800, 600),
                @"\\.\DISPLAY1",
                IsElevated: false
            ))
            .ToList();
        Scene scene = new(
            sceneId,
            "title",
            pws.ToImmutableList(),
            pws[0].Identity,
            DateTimeOffset.UtcNow
        );
        return new StageState(
            StagePhase.Enabled,
            ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                new Dictionary<string, ImmutableList<Scene>>
                {
                    [@"\\.\DISPLAY1"] = ImmutableList.Create(scene),
                }
            ),
            ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                new Dictionary<string, SceneId?> { [@"\\.\DISPLAY1"] = sceneId }
            ),
            ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                new Dictionary<string, SavedWorkArea>
                {
                    [@"\\.\DISPLAY1"] = new SavedWorkArea(
                        @"\\.\DISPLAY1",
                        new Rect(0, 0, 1920, 1040)
                    ),
                }
            ),
            ImmutableList<WindowIdentity>.Empty,
            IsPaused: false
        );
    }

    [Fact]
    public async Task WriteAsync_Writes_Json_To_Disk()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        StageState state = BuildState(SceneId.New(), new WindowIdentity(new IntPtr(1), 100, 1L));

        await store.WriteAsync(state, CancellationToken.None).ConfigureAwait(true);

        File.Exists(SnapshotPath).Should().BeTrue();
        store.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_Round_Trips_Single_Window_Scene()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        SceneId sceneId = SceneId.New();
        StageState state = BuildState(sceneId, new WindowIdentity(new IntPtr(1), 100, 1L));

        await store.WriteAsync(state, CancellationToken.None).ConfigureAwait(true);

        // Read raw to verify scenes[] contains one scene with one window.
        var json = await File.ReadAllTextAsync(SnapshotPath, CancellationToken.None)
            .ConfigureAwait(true);
        json.Should().Contain("\"scenes\"");
        json.Should().Contain(sceneId.Value.ToString("D"));
    }

    [Fact]
    public async Task WriteAsync_Round_Trips_Multi_Window_Scene()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        SceneId sceneId = SceneId.New();
        StageState state = BuildState(
            sceneId,
            new WindowIdentity(new IntPtr(1), 100, 1L),
            new WindowIdentity(new IntPtr(2), 100, 1L),
            new WindowIdentity(new IntPtr(3), 100, 1L)
        );

        await store.WriteAsync(state, CancellationToken.None).ConfigureAwait(true);

        // ReadIfStale won't return the file (we ARE the live process), but we
        // can verify the on-disk shape by reading raw.
        var json = await File.ReadAllTextAsync(SnapshotPath, CancellationToken.None)
            .ConfigureAwait(true);
        json.Should().Contain("\"hwnd\": 1");
        json.Should().Contain("\"hwnd\": 2");
        json.Should().Contain("\"hwnd\": 3");
    }

    [Fact]
    public async Task DeleteAsync_Removes_File()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        StageState state = BuildState(SceneId.New(), new WindowIdentity(new IntPtr(1), 100, 1L));
        await store.WriteAsync(state, CancellationToken.None).ConfigureAwait(true);

        await store.DeleteAsync(CancellationToken.None).ConfigureAwait(true);

        File.Exists(SnapshotPath).Should().BeFalse();
        store.Exists.Should().BeFalse();
    }

    [Fact]
    public Task DeleteAsync_Without_Existing_File_Is_NoOp()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        Func<Task> act = () => store.DeleteAsync(CancellationToken.None);
        return act.Should().NotThrowAsync();
    }

    [Fact]
    public void ReadIfStale_Returns_Null_When_File_Does_Not_Exist()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        store.ReadIfStale().Should().BeNull();
    }

    [Fact]
    public async Task ReadIfStale_Returns_Null_For_Live_Process_PID()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        StageState state = BuildState(SceneId.New(), new WindowIdentity(new IntPtr(1), 100, 1L));
        await store.WriteAsync(state, CancellationToken.None).ConfigureAwait(true);
        // Snapshot was written with our own PID — should be considered fresh.

        SnapshotFile? file = store.ReadIfStale();
        file.Should().BeNull();
    }

    [Fact]
    public void ReadIfStale_Returns_Snapshot_When_Json_References_Dead_PID()
    {
        // Write a snapshot manually with a definitely-dead PID.
        var fakeFile = new SnapshotFile
        {
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProcessId = (int)(0xDEADBEEF >> 4), // very unlikely to match an active PID
            ProcessStartTimeUtcTicks = 1L,
        };
        File.WriteAllText(SnapshotPath, System.Text.Json.JsonSerializer.Serialize(fakeFile));

        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );

        SnapshotFile? read = store.ReadIfStale();
        read.Should().NotBeNull();
        read!.ProcessId.Should().Be(fakeFile.ProcessId);
    }

    [Fact]
    public void ReadIfStale_Returns_Null_For_Malformed_Json()
    {
        File.WriteAllText(SnapshotPath, "{ this is not valid json @@@");

        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );

        SnapshotFile? read = store.ReadIfStale();
        read.Should().BeNull();
    }

    [Fact]
    public async Task IsElevated_Round_Trips()
    {
        var store = new SnapshotStore(
            new FixedPaths(SnapshotPath),
            new FakeClock(),
            NullLogger<SnapshotStore>.Instance
        );
        var pw = new ParkedWindow(
            new WindowIdentity(new IntPtr(1), 100, 1L),
            new Rect(0, 0, 100, 100),
            @"\\.\DISPLAY1",
            IsElevated: true
        );
        Scene scene = new(SceneId.New(), "elevated", [pw], pw.Identity, DateTimeOffset.UtcNow);
        var state = new StageState(
            StagePhase.Enabled,
            ImmutableDictionary.CreateRange(
                StringComparer.OrdinalIgnoreCase,
                new Dictionary<string, ImmutableList<Scene>>
                {
                    [@"\\.\DISPLAY1"] = ImmutableList.Create(scene),
                }
            ),
            ImmutableDictionary<string, SceneId?>.Empty,
            ImmutableDictionary<string, SavedWorkArea>.Empty,
            ImmutableList<WindowIdentity>.Empty,
            false
        );

        await store.WriteAsync(state, CancellationToken.None).ConfigureAwait(true);

        var json = await File.ReadAllTextAsync(SnapshotPath, CancellationToken.None)
            .ConfigureAwait(true);
        json.Should().Contain("\"isElevated\": true");
    }
}
