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
    public void Empty_IsDisabled_WithNoParkedWindows()
    {
        StageState s = StageState.Empty;

        s.Phase.Should().Be(StagePhase.Disabled);
        s.Parked.Should().BeEmpty();
        s.ActiveHwndByDevice.Should().BeEmpty();
        s.SavedWorkAreasByDevice.Should().BeEmpty();
        s.ExcludedWindows.Should().BeEmpty();
        s.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void Disabled_Must_Have_Empty_Parked_List_Invariant()
    {
        // This invariant is not enforced in the record itself (records are immutable
        // snapshots, not self-validating constructors). It is a contract upheld by
        // StageController — the test documents the expected invariant so future code
        // does not violate it silently.
        StageState s = StageState.Empty;
        (s.Phase == StagePhase.Disabled && s.Parked.IsEmpty).Should().BeTrue();
    }

    [Fact]
    public void Records_Compare_By_Value()
    {
        var id1 = new WindowIdentity(new IntPtr(1), 1000, 1234567890L);
        var id2 = new WindowIdentity(new IntPtr(1), 1000, 1234567890L);
        id1.Should().Be(id2);
    }

    [Fact]
    public void Per_Device_Dictionaries_Are_Case_Sensitive_By_Default()
    {
        // Device names like "\\\\.\\DISPLAY1" are returned by Win32 and are treated
        // as exact strings. Consumers that need case-insensitive lookup should wrap
        // explicitly.
        ImmutableDictionary<string, nint?> s = StageState.Empty.ActiveHwndByDevice.Add(
            @"\\.\DISPLAY1",
            IntPtr.Zero
        );

        s.ContainsKey(@"\\.\display1").Should().BeFalse();
        s.ContainsKey(@"\\.\DISPLAY1").Should().BeTrue();
    }

    [Fact]
    public void Per_Device_Dictionaries_Use_Ordinal_Key_Comparer()
    {
        // Guards against a well-meaning "fix" swapping the default comparer for
        // StringComparer.OrdinalIgnoreCase, which would silently break multi-monitor
        // restore (two distinct Win32 device names could then collapse into one key).
        IEqualityComparer<string> activeCmp = StageState.Empty.ActiveHwndByDevice.KeyComparer;
        IEqualityComparer<string> workAreaCmp = StageState.Empty.SavedWorkAreasByDevice.KeyComparer;

        activeCmp.Equals(@"\\.\DISPLAY1", @"\\.\display1").Should().BeFalse();
        workAreaCmp.Equals(@"\\.\DISPLAY1", @"\\.\display1").Should().BeFalse();
    }
}
