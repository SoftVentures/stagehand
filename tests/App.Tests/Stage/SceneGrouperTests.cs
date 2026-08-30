using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Services.Stage;
using FluentAssertions;
using Xunit;

namespace App.Tests.Stage;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class SceneGrouperTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow;

    private static WindowSnapshot Snapshot(int pid, IntPtr hwnd) =>
        new(
            Hwnd: hwnd,
            Title: "t",
            ClassName: "c",
            ProcessId: pid,
            ProcessStartTimeUtcTicks: 0L,
            Bounds: new Rect(0, 0, 100, 100),
            Monitor: IntPtr.Zero,
            IsVisible: true,
            IsCloaked: false,
            IsTopLevel: true,
            Style: 0,
            ExStyle: 0,
            HasOwner: false,
            ProcessName: "p"
        );

    private static Scene SceneWith(int pid, IntPtr hwnd, string device)
    {
        var id = new WindowIdentity(hwnd, pid, 0L);
        var pw = new ParkedWindow(id, new Rect(0, 0, 100, 100), device, IsElevated: false);
        return new Scene(SceneId.New(), $"pid {pid}", [pw], id, T0);
    }

    [Fact]
    public void ByProcessId_No_Existing_Scenes_Creates_New()
    {
        var sut = new SceneGrouper();
        SceneAssignment result = sut.AssignToScene(
            Snapshot(123, new IntPtr(1)),
            existingScenesOnSameMonitor: [],
            SceneGroupingMode.ByProcessId
        );
        result.CreatedNewScene.Should().BeTrue();
    }

    [Fact]
    public void ByProcessId_Existing_Scene_With_Matching_ProcessId_Joins_It()
    {
        var sut = new SceneGrouper();
        Scene existing = SceneWith(pid: 123, hwnd: new IntPtr(1), device: @"\\.\DISPLAY1");

        SceneAssignment result = sut.AssignToScene(
            Snapshot(123, new IntPtr(2)),
            existingScenesOnSameMonitor: [existing],
            SceneGroupingMode.ByProcessId
        );

        result.CreatedNewScene.Should().BeFalse();
        result.TargetScene.Should().Be(existing.Id);
    }

    [Fact]
    public void ByProcessId_Existing_Scene_With_Different_Pid_Creates_New()
    {
        var sut = new SceneGrouper();
        Scene existing = SceneWith(pid: 999, hwnd: new IntPtr(1), device: @"\\.\DISPLAY1");

        SceneAssignment result = sut.AssignToScene(
            Snapshot(123, new IntPtr(2)),
            existingScenesOnSameMonitor: [existing],
            SceneGroupingMode.ByProcessId
        );

        result.CreatedNewScene.Should().BeTrue();
        result.TargetScene.Should().NotBe(existing.Id);
    }

    [Fact]
    public void ByProcessId_Multiple_Match_Returns_First()
    {
        var sut = new SceneGrouper();
        Scene first = SceneWith(pid: 123, hwnd: new IntPtr(1), device: @"\\.\DISPLAY1");
        Scene second = SceneWith(pid: 123, hwnd: new IntPtr(2), device: @"\\.\DISPLAY1");

        SceneAssignment result = sut.AssignToScene(
            Snapshot(123, new IntPtr(3)),
            existingScenesOnSameMonitor: [first, second],
            SceneGroupingMode.ByProcessId
        );

        result.TargetScene.Should().Be(first.Id);
    }

    [Fact]
    public void Manual_Always_Creates_New_Scene()
    {
        var sut = new SceneGrouper();
        Scene existing = SceneWith(pid: 123, hwnd: new IntPtr(1), device: @"\\.\DISPLAY1");

        SceneAssignment result = sut.AssignToScene(
            Snapshot(123, new IntPtr(2)),
            existingScenesOnSameMonitor: [existing],
            SceneGroupingMode.Manual
        );

        result.CreatedNewScene.Should().BeTrue();
    }
}
