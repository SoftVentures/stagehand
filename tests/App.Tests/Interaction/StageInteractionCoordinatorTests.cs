using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using App.Core.Stage;
using App.Interop;
using App.Interop.Threading;
using App.Shell.Interaction;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interaction;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class StageInteractionCoordinatorTests
{
    private sealed class TestOverlayHost : IStageOverlayHost
    {
        public IReadOnlyList<IntPtr> ManagedMonitors => Array.Empty<IntPtr>();

        public event EventHandler<SceneClickedEventArgs>? SceneClicked;

        public void CreateForMonitor(IntPtr monitor) { }

        public void DisposeForMonitor(IntPtr monitor) { }

        public void SyncMonitor(IntPtr monitor, IReadOnlyList<Scene> scenes) { }

        public void DisposeAll() { }

        public void RaiseClicked(SceneClickedEventArgs args) => SceneClicked?.Invoke(this, args);
    }

    private static UiDispatcher CurrentDispatcher() => new(Dispatcher.CurrentDispatcher);

    private static IWinEventHookFactory ThrowingHookFactory()
    {
        // Real WinEventHook can't be constructed without a real Win32 thread —
        // make Create throw so Start() falls back to "no hook installed", which
        // is the supported failure mode (logged warning).
        var hooks = Substitute.For<IWinEventHookFactory>();
        hooks
            .When(h => h.Create(Arg.Any<WinEventSpec>()))
            .Do(_ => throw new InvalidOperationException("hook unavailable in unit tests"));
        return hooks;
    }

    [Fact]
    public async Task SceneClicked_Calls_SwapAsync_With_Resolved_Scene_And_Device()
    {
        var stage = Substitute.For<IStageController>();
        stage.CurrentState.Returns(StageState.Empty);
        var overlay = new TestOverlayHost();

        using var sut = new StageInteractionCoordinator(
            stage,
            overlay,
            ThrowingHookFactory(),
            CurrentDispatcher(),
            NullLogger<StageInteractionCoordinator>.Instance
        );
        sut.Start(); // hook install fails silently; click subscription remains

        var sceneId = SceneId.New();
        overlay.RaiseClicked(
            new SceneClickedEventArgs(
                sceneId,
                new WindowIdentity(new IntPtr(1), 1, 1),
                @"\\.\DISPLAY1"
            )
        );

        // Fire-and-forget — give the task a chance to run.
        await Task.Delay(20).ConfigureAwait(true);

        await stage
            .Received()
            .SwapAsync(sceneId, @"\\.\DISPLAY1", Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public async Task Stop_Unsubscribes_SceneClicked()
    {
        var stage = Substitute.For<IStageController>();
        stage.CurrentState.Returns(StageState.Empty);
        var overlay = new TestOverlayHost();

        using var sut = new StageInteractionCoordinator(
            stage,
            overlay,
            ThrowingHookFactory(),
            CurrentDispatcher(),
            NullLogger<StageInteractionCoordinator>.Instance
        );
        sut.Start();
        sut.Stop();

        overlay.RaiseClicked(
            new SceneClickedEventArgs(
                SceneId.New(),
                new WindowIdentity(new IntPtr(1), 1, 1),
                @"\\.\DISPLAY1"
            )
        );
        await Task.Delay(20).ConfigureAwait(true);

        await stage
            .DidNotReceive()
            .SwapAsync(Arg.Any<SceneId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ConfigureAwait(true);
    }

    [Fact]
    public void Start_Tolerates_Hook_Install_Failure()
    {
        var stage = Substitute.For<IStageController>();
        stage.CurrentState.Returns(StageState.Empty);
        var overlay = new TestOverlayHost();

        using var sut = new StageInteractionCoordinator(
            stage,
            overlay,
            ThrowingHookFactory(),
            CurrentDispatcher(),
            NullLogger<StageInteractionCoordinator>.Instance
        );

        Action act = sut.Start;
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var stage = Substitute.For<IStageController>();
        stage.CurrentState.Returns(StageState.Empty);
        var overlay = new TestOverlayHost();

        var sut = new StageInteractionCoordinator(
            stage,
            overlay,
            ThrowingHookFactory(),
            CurrentDispatcher(),
            NullLogger<StageInteractionCoordinator>.Instance
        );
        sut.Start();
        sut.Dispose();
        Action second = sut.Dispose;
        second.Should().NotThrow();
    }
}
