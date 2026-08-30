using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using App.Core.Stage;
using App.Interop;
using App.Interop.Threading;
using App.Shell.Overlay;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests.Overlay;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class StageOverlayHostTests
{
    private sealed class FakeOverlay : ISidebarOverlayHandle
    {
        public IntPtr ShownOn { get; private set; }
        public bool Released { get; private set; }
        public IReadOnlyList<Scene>? LastSync { get; private set; }

        public event EventHandler<SidebarTileClickedEventArgs>? TileClicked;

        public void ShowOn(IntPtr monitor) => ShownOn = monitor;

        public void HideAndRelease() => Released = true;

        public void SyncScenes(IReadOnlyList<Scene> scenes) => LastSync = scenes;

        public void RaiseClick(SceneId scene, WindowIdentity primary) =>
            TileClicked?.Invoke(this, new SidebarTileClickedEventArgs(scene, primary));
    }

    private sealed class StubMonitorEnumerator : MonitorEnumerator
    {
        private readonly List<MonitorDescriptor> _monitors;

        public StubMonitorEnumerator(params MonitorDescriptor[] monitors)
        {
            _monitors = monitors.ToList();
        }

        public override IReadOnlyList<MonitorDescriptor> EnumerateAll() => _monitors;
    }

    private static UiDispatcher CurrentDispatcher() => new(Dispatcher.CurrentDispatcher);

    private static MonitorDescriptor Mon(IntPtr h, string device) =>
        new(h, device, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), IsPrimary: true);

    [Fact]
    public void Initially_ManagedMonitors_Is_Empty()
    {
        var host = new StageOverlayHost(
            () => new FakeOverlay(),
            CurrentDispatcher(),
            new StubMonitorEnumerator(),
            NullLogger<StageOverlayHost>.Instance
        );
        host.ManagedMonitors.Should().BeEmpty();
    }

    [Fact]
    public void CreateForMonitor_Adds_To_ManagedMonitors_And_Calls_ShowOn()
    {
        FakeOverlay? created = null;
        var host = new StageOverlayHost(
            () =>
            {
                created = new FakeOverlay();
                return created;
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(Mon(new IntPtr(1), @"\\.\DISPLAY1")),
            NullLogger<StageOverlayHost>.Instance
        );

        host.CreateForMonitor(new IntPtr(1));

        host.ManagedMonitors.Should().Contain(new IntPtr(1));
        created!.ShownOn.Should().Be(new IntPtr(1));
    }

    [Fact]
    public void CreateForMonitor_Twice_Does_Not_Replace()
    {
        var count = 0;
        var host = new StageOverlayHost(
            () =>
            {
                count++;
                return new FakeOverlay();
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(),
            NullLogger<StageOverlayHost>.Instance
        );

        host.CreateForMonitor(new IntPtr(1));
        host.CreateForMonitor(new IntPtr(1));

        count.Should().Be(1);
    }

    [Fact]
    public void DisposeForMonitor_Removes_And_Releases()
    {
        FakeOverlay? created = null;
        var host = new StageOverlayHost(
            () =>
            {
                created = new FakeOverlay();
                return created;
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(Mon(new IntPtr(1), @"\\.\DISPLAY1")),
            NullLogger<StageOverlayHost>.Instance
        );

        host.CreateForMonitor(new IntPtr(1));
        host.DisposeForMonitor(new IntPtr(1));

        host.ManagedMonitors.Should().BeEmpty();
        created!.Released.Should().BeTrue();
    }

    [Fact]
    public void SyncMonitor_Delegates_To_Overlay()
    {
        FakeOverlay? created = null;
        var host = new StageOverlayHost(
            () =>
            {
                created = new FakeOverlay();
                return created;
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(Mon(new IntPtr(1), @"\\.\DISPLAY1")),
            NullLogger<StageOverlayHost>.Instance
        );

        host.CreateForMonitor(new IntPtr(1));

        var id = new WindowIdentity(new IntPtr(0xA), 100, 0);
        var pw = new ParkedWindow(id, new Rect(0, 0, 800, 600), @"\\.\DISPLAY1", IsElevated: false);
        Scene scene = new(SceneId.New(), "title", [pw], id, DateTimeOffset.UtcNow);

        host.SyncMonitor(new IntPtr(1), [scene]);

        created!.LastSync.Should().NotBeNull();
        created.LastSync.Should().HaveCount(1);
    }

    [Fact]
    public void SyncMonitor_Unknown_Hmonitor_Is_NoOp()
    {
        var host = new StageOverlayHost(
            () => new FakeOverlay(),
            CurrentDispatcher(),
            new StubMonitorEnumerator(),
            NullLogger<StageOverlayHost>.Instance
        );

        Action act = () => host.SyncMonitor(new IntPtr(99), Array.Empty<Scene>());
        act.Should().NotThrow();
    }

    [Fact]
    public void DisposeAll_Releases_All_Overlays()
    {
        var created = new List<FakeOverlay>();
        var host = new StageOverlayHost(
            () =>
            {
                var f = new FakeOverlay();
                created.Add(f);
                return f;
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(),
            NullLogger<StageOverlayHost>.Instance
        );
        host.CreateForMonitor(new IntPtr(1));
        host.CreateForMonitor(new IntPtr(2));

        host.DisposeAll();

        host.ManagedMonitors.Should().BeEmpty();
        created.Should().AllSatisfy(o => o.Released.Should().BeTrue());
    }

    [Fact]
    public void TileClick_Forwards_As_SceneClicked_With_DeviceName()
    {
        FakeOverlay? created = null;
        var host = new StageOverlayHost(
            () =>
            {
                created = new FakeOverlay();
                return created;
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(Mon(new IntPtr(1), @"\\.\DISPLAY1")),
            NullLogger<StageOverlayHost>.Instance
        );

        host.CreateForMonitor(new IntPtr(1));

        SceneClickedEventArgs? captured = null;
        host.SceneClicked += (_, args) => captured = args;

        SceneId sceneId = SceneId.New();
        var primary = new WindowIdentity(new IntPtr(0xA), 100, 0);
        created!.RaiseClick(sceneId, primary);

        captured.Should().NotBeNull();
        captured!.Scene.Should().Be(sceneId);
        captured.ClickedWindow.Should().Be(primary);
        captured.DeviceName.Should().Be(@"\\.\DISPLAY1");
    }

    [Fact]
    public void TileClick_Drops_When_Hmonitor_Not_Resolvable()
    {
        FakeOverlay? created = null;
        var host = new StageOverlayHost(
            () =>
            {
                created = new FakeOverlay();
                return created;
            },
            CurrentDispatcher(),
            new StubMonitorEnumerator(), // empty: hmonitor not in enumeration
            NullLogger<StageOverlayHost>.Instance
        );
        host.CreateForMonitor(new IntPtr(1));

        var raised = false;
        host.SceneClicked += (_, _) => raised = true;
        created!.RaiseClick(SceneId.New(), new WindowIdentity(new IntPtr(0xA), 100, 0));

        raised.Should().BeFalse();
    }
}
