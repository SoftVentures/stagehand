using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Shell.Monitors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Monitors;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class MonitorChangeCoordinatorTests
{
    private sealed class FakeMonitors : MonitorEnumerator
    {
        public List<MonitorDescriptor> Result { get; set; } = new();

        public override IReadOnlyList<MonitorDescriptor> EnumerateAll() => Result;
    }

    private static MonitorDescriptor Mon(IntPtr h, string name) =>
        new(h, name, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), IsPrimary: true);

    [Fact]
    public void Reconcile_When_Disabled_Is_NoOp()
    {
        var stage = Substitute.For<IStageController>();
        stage.IsEnabled.Returns(false);
        var overlay = Substitute.For<IStageOverlayHost>();

        var sut = new MonitorChangeCoordinator(
            stage,
            overlay,
            new FakeMonitors(),
            NullLogger<MonitorChangeCoordinator>.Instance
        );

        sut.Reconcile();

        overlay.DidNotReceive().CreateForMonitor(Arg.Any<IntPtr>());
        overlay.DidNotReceive().DisposeForMonitor(Arg.Any<IntPtr>());
    }

    [Fact]
    public void Reconcile_Disposes_Overlay_For_Detached_Monitor()
    {
        var stage = Substitute.For<IStageController>();
        stage.IsEnabled.Returns(true);
        var overlay = Substitute.For<IStageOverlayHost>();
        overlay.ManagedMonitors.Returns(new[] { new IntPtr(1), new IntPtr(2) });
        var monitors = new FakeMonitors { Result = { Mon(new IntPtr(1), @"\\.\DISPLAY1") } };

        var sut = new MonitorChangeCoordinator(
            stage,
            overlay,
            monitors,
            NullLogger<MonitorChangeCoordinator>.Instance
        );

        sut.Reconcile();

        overlay.Received(1).DisposeForMonitor(new IntPtr(2));
        overlay.DidNotReceive().DisposeForMonitor(new IntPtr(1));
    }

    [Fact]
    public void Reconcile_Creates_Overlay_For_New_Monitor()
    {
        var stage = Substitute.For<IStageController>();
        stage.IsEnabled.Returns(true);
        var overlay = Substitute.For<IStageOverlayHost>();
        overlay.ManagedMonitors.Returns(new[] { new IntPtr(1) });
        var monitors = new FakeMonitors
        {
            Result = { Mon(new IntPtr(1), @"\\.\DISPLAY1"), Mon(new IntPtr(2), @"\\.\DISPLAY2") },
        };

        var sut = new MonitorChangeCoordinator(
            stage,
            overlay,
            monitors,
            NullLogger<MonitorChangeCoordinator>.Instance
        );

        sut.Reconcile();

        overlay.Received(1).CreateForMonitor(new IntPtr(2));
        overlay.DidNotReceive().CreateForMonitor(new IntPtr(1));
    }

    [Fact]
    public void Reconcile_NoChange_Does_Nothing()
    {
        var stage = Substitute.For<IStageController>();
        stage.IsEnabled.Returns(true);
        var overlay = Substitute.For<IStageOverlayHost>();
        overlay.ManagedMonitors.Returns(new[] { new IntPtr(1) });
        var monitors = new FakeMonitors { Result = { Mon(new IntPtr(1), @"\\.\DISPLAY1") } };

        var sut = new MonitorChangeCoordinator(
            stage,
            overlay,
            monitors,
            NullLogger<MonitorChangeCoordinator>.Instance
        );

        sut.Reconcile();

        overlay.DidNotReceive().CreateForMonitor(Arg.Any<IntPtr>());
        overlay.DidNotReceive().DisposeForMonitor(Arg.Any<IntPtr>());
    }
}
