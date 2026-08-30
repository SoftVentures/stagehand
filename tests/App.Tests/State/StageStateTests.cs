using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using FluentAssertions;
using Xunit;

namespace App.Tests.State;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class StageStateTests
{
    [Fact]
    public void Empty_IsDisabled_WithNoScenes()
    {
        StageState s = StageState.Empty;

        s.Phase.Should().Be(StagePhase.Disabled);
        s.ScenesByDevice.Should().BeEmpty();
        s.ActiveSceneByDevice.Should().BeEmpty();
        s.SavedWorkAreasByDevice.Should().BeEmpty();
        s.ExcludedWindows.Should().BeEmpty();
        s.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void Records_Compare_By_Value()
    {
        var id1 = new WindowIdentity(new IntPtr(1), 1000, 1234567890L);
        var id2 = new WindowIdentity(new IntPtr(1), 1000, 1234567890L);
        id1.Should().Be(id2);
    }

    [Fact]
    public void SceneId_New_Returns_Unique_Values()
    {
        SceneId a = SceneId.New();
        SceneId b = SceneId.New();
        a.Should().NotBe(b);
    }

    [Fact]
    public void Scene_Constructor_Rejects_Empty_Window_List()
    {
        Action act = () =>
        {
            _ = new Scene(
                SceneId.New(),
                "x",
                ImmutableList<ParkedWindow>.Empty,
                new WindowIdentity(IntPtr.Zero, 0, 0),
                DateTimeOffset.UtcNow
            );
        };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Scene_Constructor_Rejects_Primary_Not_In_Windows()
    {
        var pw = new ParkedWindow(
            new WindowIdentity(new IntPtr(1), 1, 1L),
            new App.Interop.Rect(0, 0, 100, 100),
            @"\\.\DISPLAY1",
            IsElevated: false
        );
        Action act = () =>
        {
            _ = new Scene(
                SceneId.New(),
                "x",
                [pw],
                new WindowIdentity(new IntPtr(2), 2, 2L), // not in list
                DateTimeOffset.UtcNow
            );
        };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Scene_Constructor_Accepts_Primary_That_Is_In_Windows()
    {
        var id = new WindowIdentity(new IntPtr(1), 1, 1L);
        var pw = new ParkedWindow(
            id,
            new App.Interop.Rect(0, 0, 100, 100),
            @"\\.\DISPLAY1",
            IsElevated: false
        );
        Action act = () =>
        {
            _ = new Scene(SceneId.New(), "x", [pw], id, DateTimeOffset.UtcNow);
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void ParkedWindow_Records_Compare_By_Value()
    {
        var id = new WindowIdentity(new IntPtr(1), 1, 1L);
        var rect = new App.Interop.Rect(0, 0, 100, 100);
        var a = new ParkedWindow(id, rect, @"\\.\DISPLAY1", IsElevated: false);
        var b = new ParkedWindow(id, rect, @"\\.\DISPLAY1", IsElevated: false);
        a.Should().Be(b);
    }

    [Fact]
    public void ParkedWindow_IsElevated_Round_Trips()
    {
        var pw = new ParkedWindow(
            new WindowIdentity(new IntPtr(1), 1, 1L),
            new App.Interop.Rect(0, 0, 100, 100),
            @"\\.\DISPLAY1",
            IsElevated: true
        );
        pw.IsElevated.Should().BeTrue();
    }

    [Fact]
    public void Per_Device_Dictionaries_Are_Case_Sensitive_By_Default()
    {
        ImmutableDictionary<string, SceneId?> s = StageState.Empty.ActiveSceneByDevice.Add(
            @"\\.\DISPLAY1",
            null
        );

        s.ContainsKey(@"\\.\display1").Should().BeFalse();
        s.ContainsKey(@"\\.\DISPLAY1").Should().BeTrue();
    }

    [Fact]
    public void Per_Device_Dictionaries_Use_Ordinal_Key_Comparer()
    {
        // Guards against a well-meaning "fix" swapping the default comparer for
        // OrdinalIgnoreCase, which would silently break multi-monitor restore
        // (two distinct Win32 device names could then collapse into one key).
        IEqualityComparer<string> activeCmp = StageState.Empty.ActiveSceneByDevice.KeyComparer;
        IEqualityComparer<string> scenesCmp = StageState.Empty.ScenesByDevice.KeyComparer;
        IEqualityComparer<string> workAreaCmp = StageState.Empty.SavedWorkAreasByDevice.KeyComparer;

        activeCmp.Equals(@"\\.\DISPLAY1", @"\\.\display1").Should().BeFalse();
        scenesCmp.Equals(@"\\.\DISPLAY1", @"\\.\display1").Should().BeFalse();
        workAreaCmp.Equals(@"\\.\DISPLAY1", @"\\.\display1").Should().BeFalse();
    }

    [Fact]
    public void StageTransitionException_Records_From_To()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new StageTransitionException(StagePhase.Disabled, StagePhase.Enabled, inner);
        ex.From.Should().Be(StagePhase.Disabled);
        ex.To.Should().Be(StagePhase.Enabled);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
