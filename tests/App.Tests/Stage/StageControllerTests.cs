using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Interop.Errors;
using App.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Stage;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Test fixtures hold the controller for the duration of a single test; relying on test-runner cleanup is acceptable."
)]
public sealed class StageControllerTests
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";

    private static readonly IntPtr H1 = new(0x10001);
    private static readonly IntPtr H2 = new(0x10002);

    // ---------- Helpers ----------

    private sealed class FakeMonitors : MonitorEnumerator
    {
        public List<MonitorDescriptor> Result { get; }

        public FakeMonitors(params MonitorDescriptor[] monitors)
        {
            Result = monitors.ToList();
        }

        public override IReadOnlyList<MonitorDescriptor> EnumerateAll() => Result;
    }

    private static MonitorDescriptor Mon(
        IntPtr h,
        string device,
        int x = 0,
        int y = 0,
        int w = 1920,
        int hgt = 1080
    ) => new(h, device, new Rect(x, y, w, hgt), new Rect(x, y, w, hgt - 40), IsPrimary: true);

    private static WindowSnapshot Win(
        IntPtr hwnd,
        int pid,
        IntPtr monitor,
        string title = "t",
        long style = 0,
        string? processName = null
    ) =>
        new(
            Hwnd: hwnd,
            Title: title,
            ClassName: "c",
            ProcessId: pid,
            ProcessStartTimeUtcTicks: 0L,
            Bounds: new Rect(100, 100, 800, 600),
            Monitor: monitor,
            IsVisible: true,
            IsCloaked: false,
            IsTopLevel: true,
            Style: style,
            ExStyle: 0,
            HasOwner: false,
            // Derive ProcessName from pid so the SceneGrouper's
            // ProcessName-first matching agrees with the by-pid intent of
            // these fixtures: same pid → same name → same scene; different
            // pids → different names → distinct scenes. Tests that need an
            // explicit name (e.g. multi-process apps sharing a name) pass
            // one in.
            ProcessName: processName ?? $"p{pid}"
        );

    private sealed class Fixture
    {
        public IWindowEnumerator Enumerator { get; }
        public IWindowFilter Filter { get; }
        public IWindowController Windows { get; }
        public IWorkAreaManager WorkArea { get; }
        public IStageOverlayHost Overlay { get; }
        public ISceneGrouper Grouper { get; } = new App.Services.Stage.SceneGrouper();
        public ISceneSwapExecutor Executor { get; }
        public ISnapshotStore Snapshot { get; }
        public FakeMonitors Monitors { get; }
        public FakeClock Clock { get; } = new();

        public StageController Build()
        {
            return new StageController(
                Enumerator,
                Filter,
                Windows,
                WorkArea,
                Overlay,
                Grouper,
                Executor,
                Snapshot,
                Monitors,
                Clock,
                NullLogger<StageController>.Instance
            );
        }

        public Fixture(params MonitorDescriptor[] monitors)
        {
            Enumerator = Substitute.For<IWindowEnumerator>();
            Filter = Substitute.For<IWindowFilter>();
            Filter.IsManageable(Arg.Any<WindowSnapshot>()).Returns(true);
            Windows = Substitute.For<IWindowController>();
            WorkArea = Substitute.For<IWorkAreaManager>();
            Overlay = Substitute.For<IStageOverlayHost>();
            Executor = Substitute.For<ISceneSwapExecutor>();
            Snapshot = Substitute.For<ISnapshotStore>();
            Snapshot
                .WriteAsync(Arg.Any<StageState>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            Snapshot.DeleteAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Monitors = new FakeMonitors(monitors);
        }

        public void EnumerateReturns(params WindowSnapshot[] windows)
        {
            Enumerator.GetManageableWindows().Returns(windows);
        }
    }

    // ---------- Construction / state ----------

    [Fact]
    public void Initial_State_Is_Empty_Disabled()
    {
        var fx = new Fixture(Mon(H1, Display1));
        using var sut = fx.Build();
        sut.IsEnabled.Should().BeFalse();
        sut.CurrentState.Should().BeSameAs(StageState.Empty);
    }

    // ---------- EnableAsync ----------

    [Fact]
    public async Task EnableAsync_Single_Window_Single_Monitor_Builds_Active_Scene()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), pid: 100, monitor: H1));

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.IsEnabled.Should().BeTrue();
        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);
        sut.CurrentState.ActiveSceneByDevice[Display1].Should().NotBeNull();
    }

    [Fact]
    public async Task EnableAsync_Two_Windows_Same_Process_Form_One_Scene()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), pid: 100, monitor: H1, title: "Notepad-1"),
            Win(new IntPtr(2), pid: 100, monitor: H1, title: "Notepad-2")
        );

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);
        sut.CurrentState.ScenesByDevice[Display1][0].Windows.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnableAsync_Two_Windows_Same_ProcessName_Different_Pids_Form_One_Scene()
    {
        // Regression: Chromium-based browsers (Edge, Chrome) spawn one
        // process per top-level window. PID-only grouping showed each
        // window as a duplicate tile; ProcessName-aware grouping merges
        // them into a single scene.
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(
                new IntPtr(1),
                pid: 1100,
                monitor: H1,
                title: "Edge - Tab 1",
                processName: "msedge"
            ),
            Win(new IntPtr(2), pid: 1200, monitor: H1, title: "Edge - Tab 2", processName: "msedge")
        );

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);
        sut.CurrentState.ScenesByDevice[Display1][0].Windows.Should().HaveCount(2);
    }

    [Fact]
    public async Task IngestNewWindowsAsync_Adds_Newly_Opened_Window_As_New_Scene()
    {
        // Regression: opening a new app after Enable must show up as a tile
        // without forcing the user to Disable + re-Enable.
        var fx = new Fixture(Mon(H1, Display1));
        WindowSnapshot first = Win(new IntPtr(1), pid: 100, monitor: H1, title: "FirstApp");
        fx.EnumerateReturns(first);
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Simulate a newly-opened second app from a different process.
        WindowSnapshot second = Win(new IntPtr(2), pid: 200, monitor: H1, title: "SecondApp");
        fx.EnumerateReturns(first, second);

        await sut.IngestNewWindowsAsync(CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(2);
        fx.Windows.Received(1).Park(new IntPtr(2), Arg.Any<App.Interop.Rect>());
    }

    [Fact]
    public async Task IngestNewWindowsAsync_Joins_Same_Process_Window_To_Existing_Scene()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), pid: 100, monitor: H1, title: "Notepad-1"));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);
        fx.Windows.ClearReceivedCalls();

        // New Notepad window from the same process. The first scene was made
        // active at Enable, so the new window joins the ACTIVE scene and must
        // NOT be parked — parking it would hide a window the user expects to
        // see (it would otherwise show up only as a sidebar tile while the
        // siblings of the same scene live in the main area).
        fx.EnumerateReturns(
            Win(new IntPtr(1), pid: 100, monitor: H1, title: "Notepad-1"),
            Win(new IntPtr(2), pid: 100, monitor: H1, title: "Notepad-2")
        );

        await sut.IngestNewWindowsAsync(CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);
        sut.CurrentState.ScenesByDevice[Display1][0].Windows.Should().HaveCount(2);
        fx.Windows.DidNotReceive().Park(new IntPtr(2), Arg.Any<App.Interop.Rect>());
    }

    [Fact]
    public async Task IngestNewWindowsAsync_Disabled_IsNoOp()
    {
        var fx = new Fixture(Mon(H1, Display1));
        using var sut = fx.Build();

        await sut.IngestNewWindowsAsync(CancellationToken.None).ConfigureAwait(true);

        sut.IsEnabled.Should().BeFalse();
        fx.Windows.DidNotReceive().Park(Arg.Any<IntPtr>(), Arg.Any<App.Interop.Rect>());
    }

    [Fact]
    public async Task EnableAsync_Skips_Windows_Rejected_By_Filter()
    {
        // Regression: StageController must apply IWindowFilter to the raw
        // enumerator output. Without this, system tray helpers, hidden
        // child popups and tooltips become "scenes" — observed in the wild
        // as 113 scenes from 3 user-visible windows.
        var fx = new Fixture(Mon(H1, Display1));
        WindowSnapshot keep = Win(new IntPtr(1), pid: 100, monitor: H1, title: "Real");
        WindowSnapshot drop = Win(new IntPtr(2), pid: 200, monitor: H1, title: "Phantom");
        fx.EnumerateReturns(keep, drop);
        fx.Filter.IsManageable(keep).Returns(true);
        fx.Filter.IsManageable(drop).Returns(false);

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);
        sut.CurrentState.ScenesByDevice[Display1][0].Primary.Hwnd.Should().Be(new IntPtr(1));
    }

    [Fact]
    public async Task EnableAsync_Two_Windows_Different_Process_Form_Two_Scenes()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), pid: 100, monitor: H1),
            Win(new IntPtr(2), pid: 200, monitor: H1)
        );

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(2);
    }

    [Fact]
    public async Task EnableAsync_First_Visible_Window_Becomes_Active()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), pid: 100, monitor: H1),
            Win(new IntPtr(2), pid: 200, monitor: H1)
        );
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        SceneId? active = sut.CurrentState.ActiveSceneByDevice[Display1];
        Scene activeScene = sut.CurrentState.ScenesByDevice[Display1].First(s => s.Id == active);
        activeScene.Primary.Hwnd.Should().Be(new IntPtr(1));
    }

    [Fact]
    public async Task EnableAsync_Skips_Minimised_Windows_For_Active_Pick()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(
                new IntPtr(1),
                pid: 100,
                monitor: H1,
                style: 0x20000000L /* WS_MINIMIZE */
            ),
            Win(new IntPtr(2), pid: 200, monitor: H1)
        );
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        SceneId? active = sut.CurrentState.ActiveSceneByDevice[Display1];
        Scene activeScene = sut.CurrentState.ScenesByDevice[Display1].First(s => s.Id == active);
        activeScene.Primary.Hwnd.Should().Be(new IntPtr(2));
    }

    [Fact]
    public async Task EnableAsync_Sets_WorkArea_Per_Monitor()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        fx.WorkArea.Received(1).SetWorkArea(H1, Arg.Any<Rect>());
    }

    [Fact]
    public async Task EnableAsync_Parks_Non_Active_Scenes()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H1));
        using var sut = fx.Build();

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Only the second window (in the parked scene) gets parked; the first is the active primary.
        fx.Windows.Received(1).Park(new IntPtr(2), Arg.Any<Rect>());
        fx.Windows.DidNotReceive().Park(new IntPtr(1), Arg.Any<Rect>());
    }

    [Fact]
    public async Task EnableAsync_Brings_Active_Primary_To_Front()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        fx.Windows.Received(1).BringToFront(new IntPtr(1));
    }

    [Fact]
    public async Task EnableAsync_Creates_Overlay_For_Each_Monitor()
    {
        var fx = new Fixture(Mon(H1, Display1, w: 1920), Mon(H2, Display2, x: 1920, w: 1920));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H2));
        using var sut = fx.Build();

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        fx.Overlay.Received(1).CreateForMonitor(H1);
        fx.Overlay.Received(1).CreateForMonitor(H2);
    }

    [Fact]
    public async Task EnableAsync_Writes_Snapshot()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        await fx
            .Snapshot.Received(1)
            .WriteAsync(Arg.Any<StageState>(), Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task EnableAsync_Idempotent_When_Already_Enabled()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Snapshot called only once; second EnableAsync is a no-op.
        await fx
            .Snapshot.Received(1)
            .WriteAsync(Arg.Any<StageState>(), Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task EnableAsync_Marks_Elevated_Window_When_Park_Throws_ElevationBoundary()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), 100, H1), // active
            Win(new IntPtr(2), 200, H1) // will throw
        );
        fx.Windows.When(w => w.Park(new IntPtr(2), Arg.Any<Rect>()))
            .Do(_ => throw new ElevationBoundaryException("denied"));

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.IsEnabled.Should().BeTrue();
        ParkedWindow elevated = sut
            .CurrentState.ScenesByDevice[Display1]
            .SelectMany(s => s.Windows)
            .First(pw => pw.Identity.Hwnd == new IntPtr(2));
        elevated.IsElevated.Should().BeTrue();
    }

    [Fact]
    public async Task EnableAsync_Rolls_Back_When_Snapshot_Write_Throws()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H1));
        fx.Snapshot.WriteAsync(Arg.Any<StageState>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("disk full")));

        using var sut = fx.Build();
        Func<Task> act = () => sut.EnableAsync(CancellationToken.None);

        await act.Should().ThrowAsync<StageTransitionException>().ConfigureAwait(true);

        sut.IsEnabled.Should().BeFalse();
        sut.CurrentState.Phase.Should().Be(StagePhase.Disabled);
        // The parked window was restored as part of rollback.
        fx.Windows.Received().RestorePosition(new IntPtr(2), Arg.Any<Rect>());
        // Work areas restored.
        fx.WorkArea.Received().RestoreAll();
    }

    [Fact]
    public async Task EnableAsync_Raises_StateChanged_Multiple_Times()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();

        var transitions = new List<StagePhase>();
        sut.StateChanged += (_, s) => transitions.Add(s.Phase);

        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Should pass through Enabling and end at Enabled.
        transitions.Should().Contain(StagePhase.Enabling);
        transitions.Should().Contain(StagePhase.Enabled);
    }

    // ---------- DisableAsync ----------

    [Fact]
    public async Task DisableAsync_Restores_All_Windows()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        await sut.DisableAsync(CancellationToken.None).ConfigureAwait(true);

        sut.IsEnabled.Should().BeFalse();
        // Both windows have RestorePosition called on Disable.
        fx.Windows.Received().RestorePosition(new IntPtr(1), Arg.Any<Rect>());
        fx.Windows.Received().RestorePosition(new IntPtr(2), Arg.Any<Rect>());
    }

    [Fact]
    public async Task DisableAsync_Disposes_All_Overlays()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        await sut.DisableAsync(CancellationToken.None).ConfigureAwait(true);

        fx.Overlay.Received(1).DisposeAll();
    }

    [Fact]
    public async Task DisableAsync_Restores_WorkAreas()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        await sut.DisableAsync(CancellationToken.None).ConfigureAwait(true);

        fx.WorkArea.Received().RestoreAll();
    }

    [Fact]
    public async Task DisableAsync_Deletes_Snapshot()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        await sut.DisableAsync(CancellationToken.None).ConfigureAwait(true);

        await fx
            .Snapshot.Received(1)
            .DeleteAsync(Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task DisableAsync_Idempotent_When_Already_Disabled()
    {
        var fx = new Fixture(Mon(H1, Display1));
        using var sut = fx.Build();

        await sut.DisableAsync(CancellationToken.None).ConfigureAwait(true);

        await fx
            .Snapshot.DidNotReceive()
            .DeleteAsync(Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task DisableAsync_Skips_Elevated_Windows_From_Restore()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H1));
        fx.Windows.When(w => w.Park(new IntPtr(2), Arg.Any<Rect>()))
            .Do(_ => throw new ElevationBoundaryException("denied"));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);
        fx.Windows.ClearReceivedCalls();

        await sut.DisableAsync(CancellationToken.None).ConfigureAwait(true);

        fx.Windows.DidNotReceive().RestorePosition(new IntPtr(2), Arg.Any<Rect>());
    }

    // ---------- SwapAsync ----------

    [Fact]
    public async Task SwapAsync_NoOp_When_Disabled()
    {
        var fx = new Fixture(Mon(H1, Display1));
        using var sut = fx.Build();

        await sut.SwapAsync(SceneId.New(), Display1, CancellationToken.None).ConfigureAwait(true);

        await fx
            .Executor.DidNotReceive()
            .RunAsync(
                Arg.Any<SceneSwapPlan>(),
                Arg.Any<AnimationSpeed>(),
                Arg.Any<CancellationToken>()
            )
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task SwapAsync_NoOp_When_Target_Not_Found()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        await sut.SwapAsync(SceneId.New(), Display1, CancellationToken.None).ConfigureAwait(true);

        await fx
            .Executor.DidNotReceive()
            .RunAsync(
                Arg.Any<SceneSwapPlan>(),
                Arg.Any<AnimationSpeed>(),
                Arg.Any<CancellationToken>()
            )
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task SwapAsync_Updates_Active_Scene_Atomically_Then_Runs_Executor()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Identify the parked scene.
        SceneId? active = sut.CurrentState.ActiveSceneByDevice[Display1];
        Scene parked = sut.CurrentState.ScenesByDevice[Display1].First(s => s.Id != active);

        await sut.SwapAsync(parked.Id, Display1, CancellationToken.None).ConfigureAwait(true);

        sut.CurrentState.ActiveSceneByDevice[Display1].Should().Be(parked.Id);
        await fx
            .Executor.Received(1)
            .RunAsync(Arg.Any<SceneSwapPlan>(), AnimationSpeed.Off, Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task SwapAsync_Plan_Includes_All_Windows_Of_Multi_Window_Incoming_Scene()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), 100, H1),
            Win(new IntPtr(2), 200, H1, title: "n1"),
            Win(new IntPtr(3), 200, H1, title: "n2") // same pid, joins the second scene
        );
        SceneSwapPlan? capturedPlan = null;
        fx.Executor.RunAsync(
                Arg.Do<SceneSwapPlan>(p => capturedPlan = p),
                Arg.Any<AnimationSpeed>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);
        SceneId? active = sut.CurrentState.ActiveSceneByDevice[Display1];
        Scene parked = sut.CurrentState.ScenesByDevice[Display1].First(s => s.Id != active);
        parked.Windows.Should().HaveCount(2);

        await sut.SwapAsync(parked.Id, Display1, CancellationToken.None).ConfigureAwait(true);

        capturedPlan.Should().NotBeNull();
        capturedPlan!.IncomingMoves.Should().HaveCount(2);
    }

    [Fact]
    public async Task SwapByWindowAsync_For_Parked_Window_Triggers_Swap()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1), Win(new IntPtr(2), 200, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        var parkedHwnd = new WindowIdentity(new IntPtr(2), 200, 0);

        await sut.SwapByWindowAsync(parkedHwnd, CancellationToken.None).ConfigureAwait(true);

        await fx
            .Executor.Received(1)
            .RunAsync(
                Arg.Any<SceneSwapPlan>(),
                Arg.Any<AnimationSpeed>(),
                Arg.Any<CancellationToken>()
            )
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task SwapByWindowAsync_For_Unknown_Window_Is_NoOp()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        var unknown = new WindowIdentity(new IntPtr(0xFEED), 999, 0);
        await sut.SwapByWindowAsync(unknown, CancellationToken.None).ConfigureAwait(true);

        await fx
            .Executor.DidNotReceive()
            .RunAsync(
                Arg.Any<SceneSwapPlan>(),
                Arg.Any<AnimationSpeed>(),
                Arg.Any<CancellationToken>()
            )
            .ConfigureAwait(true);
    }

    // ---------- MoveWindowToSceneAsync ----------

    [Fact]
    public async Task MoveWindowToSceneAsync_To_Existing_Scene_Merges_It()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), 100, H1),
            Win(new IntPtr(2), 200, H1) // separate scene
        );
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        SceneId activeSceneId = sut.CurrentState.ActiveSceneByDevice[Display1]!.Value;
        var movedIdentity = new WindowIdentity(new IntPtr(2), 200, 0);

        await sut.MoveWindowToSceneAsync(movedIdentity, activeSceneId, CancellationToken.None)
            .ConfigureAwait(true);

        // After the move, only one scene exists on the monitor (target absorbed source).
        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);
        sut.CurrentState.ScenesByDevice[Display1][0].Windows.Should().HaveCount(2);
    }

    [Fact]
    public async Task MoveWindowToSceneAsync_With_Null_Target_Mints_New_Scene()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), 100, H1, title: "n1"),
            Win(new IntPtr(2), 100, H1, title: "n2") // same pid → same scene
        );
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);
        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(1);

        var moved = new WindowIdentity(new IntPtr(2), 100, 0);
        await sut.MoveWindowToSceneAsync(moved, targetScene: null, CancellationToken.None)
            .ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(2);
    }

    [Fact]
    public async Task MoveWindowToSceneAsync_Promotes_Next_Window_When_Primary_Moves_Out()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(
            Win(new IntPtr(1), 100, H1, title: "n1"),
            Win(new IntPtr(2), 100, H1, title: "n2")
        );
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Move the primary out (window 1).
        var primary = new WindowIdentity(new IntPtr(1), 100, 0);
        await sut.MoveWindowToSceneAsync(primary, targetScene: null, CancellationToken.None)
            .ConfigureAwait(true);

        // The remaining scene (window 2) should have window 2 promoted to primary.
        Scene remaining = sut
            .CurrentState.ScenesByDevice[Display1]
            .First(s => s.Windows.Any(pw => pw.Identity.Hwnd == new IntPtr(2)));
        remaining.Primary.Hwnd.Should().Be(new IntPtr(2));
    }

    [Fact]
    public async Task MoveWindowToSceneAsync_For_Unknown_Window_Is_NoOp()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);
        var sceneCountBefore = sut.CurrentState.ScenesByDevice[Display1].Count;

        var unknown = new WindowIdentity(new IntPtr(0xFEED), 999, 0);
        await sut.MoveWindowToSceneAsync(unknown, targetScene: null, CancellationToken.None)
            .ConfigureAwait(true);

        sut.CurrentState.ScenesByDevice[Display1].Should().HaveCount(sceneCountBefore);
    }

    // ---------- Concurrency ----------

    [Fact]
    public async Task Concurrent_Enable_Calls_Are_Serialised()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));

        // Make WriteAsync block until we say so. The first EnableAsync call
        // is parked deep inside its critical section; the second must wait
        // on the state semaphore — proven by inspecting that
        // GetManageableWindows was called exactly once while we held the
        // gate. (If serialisation were missing, the second call would have
        // blown past Phase==Enabling and re-entered enumeration.)
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fx.Snapshot.WriteAsync(Arg.Any<StageState>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);

        using var sut = fx.Build();

        Task t1 = sut.EnableAsync(CancellationToken.None);
        Task t2 = sut.EnableAsync(CancellationToken.None);

        // Give the scheduler a moment so t1 has reached WriteAsync (parked)
        // and t2 has had its chance to either queue or no-op.
        await Task.Delay(50).ConfigureAwait(true);

        t1.IsCompleted.Should().BeFalse("t1 is parked inside WriteAsync");
        // While t1 holds the state gate, t2 must NOT have run enumeration —
        // either because it's queued on the gate (mutex case) or because
        // it sees Phase != Disabled (post-release no-op case). With the
        // gate held, only t1 ever reached enumeration.
        fx.Enumerator.Received(1).GetManageableWindows();

        gate.SetResult();
        await Task.WhenAll(t1, t2).ConfigureAwait(true);

        // Snapshot is written exactly once (t2 sees Phase==Enabled and no-ops).
        await fx
            .Snapshot.Received(1)
            .WriteAsync(Arg.Any<StageState>(), Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    // ---------- C2 regression (cross-monitor MoveWindowToScene) ----------

    [Fact]
    public async Task MoveWindowToScene_Cross_Monitor_Falls_Back_To_New_Scene_On_Source()
    {
        var fx = new Fixture(Mon(H1, Display1, x: 0, w: 1920), Mon(H2, Display2, x: 1920, w: 1920));
        fx.EnumerateReturns(
            Win(new IntPtr(1), pid: 100, monitor: H1, title: "a"),
            Win(new IntPtr(2), pid: 200, monitor: H2, title: "b")
        );
        using var sut = fx.Build();
        await sut.EnableAsync(CancellationToken.None).ConfigureAwait(true);

        // Identify scenes per monitor.
        Scene sceneOnDisplay2 = sut.CurrentState.ScenesByDevice[Display2][0];
        var windowOnDisplay1 = new WindowIdentity(new IntPtr(1), 100, 0);

        // Try to move the Display1 window into Display2's scene.
        await sut.MoveWindowToSceneAsync(
                windowOnDisplay1,
                sceneOnDisplay2.Id,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // Display2's scene must NOT contain the moved window.
        sut.CurrentState.ScenesByDevice[Display2]
            .SelectMany(s => s.Windows)
            .Should()
            .NotContain(pw => pw.Identity == windowOnDisplay1);
        // The moved window lives on Display1, in a fresh scene.
        sut.CurrentState.ScenesByDevice[Display1]
            .SelectMany(s => s.Windows)
            .Should()
            .Contain(pw => pw.Identity == windowOnDisplay1);
    }

    // ---------- C3 regression (StateChanged event re-entrancy) ----------

    [Fact]
    public async Task StateChanged_Subscriber_Calling_Back_Does_Not_Deadlock()
    {
        var fx = new Fixture(Mon(H1, Display1));
        fx.EnumerateReturns(Win(new IntPtr(1), 100, H1));
        using var sut = fx.Build();

        // Subscriber that calls SwapAsync synchronously — would deadlock if
        // StateChanged were raised inside the state semaphore.
        sut.StateChanged += (_, s) =>
        {
            if (s.Phase == StagePhase.Enabled)
            {
                // Fire and forget; just observe that the call doesn't block.
                _ = sut.SwapAsync(SceneId.New(), Display1, CancellationToken.None);
            }
        };

        // The outer Enable must complete; if the event fired inside the gate,
        // the SwapAsync call would block waiting for the swap semaphore which
        // would in turn wait for the state mutation that *just* happened.
        Task enable = sut.EnableAsync(CancellationToken.None);
        Task winnerTask = await Task.WhenAny(enable, Task.Delay(2000)).ConfigureAwait(true);
        winnerTask
            .Should()
            .BeSameAs(enable, "EnableAsync must not deadlock on a re-entrant subscriber");
        await enable.ConfigureAwait(true);
    }
}
