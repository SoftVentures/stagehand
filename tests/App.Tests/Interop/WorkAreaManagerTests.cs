using System.Diagnostics.CodeAnalysis;
using App.Interop;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests.Interop;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class WorkAreaManagerTests
{
    private static MonitorDescriptor M(IntPtr h, string device, Rect work, Rect? full = null) =>
        new(h, device, full ?? work, work, IsPrimary: false);

    [Fact]
    public void GetWorkArea_Returns_Empty_For_Unknown_Hmonitor()
    {
        IReadOnlyList<MonitorDescriptor> monitors = Array.Empty<MonitorDescriptor>();
        var sut = new WorkAreaManager(
            () => monitors,
            _ => true,
            NullLogger<WorkAreaManager>.Instance
        );

        sut.GetWorkArea(new IntPtr(0xDEAD)).Should().Be(Rect.Empty);
    }

    [Fact]
    public void GetWorkArea_Returns_Descriptor_WorkArea()
    {
        Rect expected = new(10, 20, 1900, 1000);
        var monitors = new[] { M(new IntPtr(1), @"\\.\DISPLAY1", expected) };
        var sut = new WorkAreaManager(
            () => monitors,
            _ => true,
            NullLogger<WorkAreaManager>.Instance
        );

        sut.GetWorkArea(new IntPtr(1)).Should().Be(expected);
    }

    [Fact]
    public void SetWorkArea_Captures_Original_Then_Applies()
    {
        Rect original = new(0, 0, 1920, 1040);
        var monitors = new[] { M(new IntPtr(1), @"\\.\DISPLAY1", original) };
        var applied = new List<Rect>();
        var sut = new WorkAreaManager(
            () => monitors,
            r =>
            {
                applied.Add(r);
                return true;
            },
            NullLogger<WorkAreaManager>.Instance
        );

        Rect newRect = new(160, 0, 1760, 1040); // sidebar 160 wide on the left
        sut.SetWorkArea(new IntPtr(1), newRect);

        applied.Should().ContainSingle().Which.Should().Be(newRect);
        sut.SavedOriginalsForTesting().Should().ContainKey(@"\\.\DISPLAY1");
        sut.SavedOriginalsForTesting()[@"\\.\DISPLAY1"].Should().Be(original);
    }

    [Fact]
    public void SetWorkArea_Twice_Keeps_First_Original()
    {
        Rect original = new(0, 0, 1920, 1040);
        var monitors = new[] { M(new IntPtr(1), @"\\.\DISPLAY1", original) };
        var sut = new WorkAreaManager(
            () => monitors,
            _ => true,
            NullLogger<WorkAreaManager>.Instance
        );

        sut.SetWorkArea(new IntPtr(1), new Rect(100, 0, 1820, 1040));
        sut.SetWorkArea(new IntPtr(1), new Rect(200, 0, 1720, 1040));

        sut.SavedOriginalsForTesting()[@"\\.\DISPLAY1"].Should().Be(original);
    }

    [Fact]
    public void SetWorkArea_Unknown_Hmonitor_Is_NoOp()
    {
        IReadOnlyList<MonitorDescriptor> monitors = Array.Empty<MonitorDescriptor>();
        var applied = new List<Rect>();
        var sut = new WorkAreaManager(
            () => monitors,
            r =>
            {
                applied.Add(r);
                return true;
            },
            NullLogger<WorkAreaManager>.Instance
        );

        sut.SetWorkArea(new IntPtr(99), new Rect(0, 0, 100, 100));
        applied.Should().BeEmpty();
        sut.SavedOriginalsForTesting().Should().BeEmpty();
    }

    [Fact]
    public void RestoreAll_Without_SetWorkArea_Is_NoOp()
    {
        var sut = new WorkAreaManager(
            Array.Empty<MonitorDescriptor>,
            _ => true,
            NullLogger<WorkAreaManager>.Instance
        );
        Action act = sut.RestoreAll;
        act.Should().NotThrow();
    }

    [Fact]
    public void RestoreAll_Reapplies_Each_Original_Per_Device()
    {
        Rect orig1 = new(0, 0, 1920, 1040);
        Rect orig2 = new(1920, 0, 2560, 1400);
        var monitors = new[]
        {
            M(new IntPtr(1), @"\\.\DISPLAY1", orig1),
            M(new IntPtr(2), @"\\.\DISPLAY2", orig2),
        };
        var applied = new List<Rect>();
        var sut = new WorkAreaManager(
            () => monitors,
            r =>
            {
                applied.Add(r);
                return true;
            },
            NullLogger<WorkAreaManager>.Instance
        );

        sut.SetWorkArea(new IntPtr(1), new Rect(160, 0, 1760, 1040));
        sut.SetWorkArea(new IntPtr(2), new Rect(2080, 0, 2400, 1400));
        applied.Clear();

        sut.RestoreAll();

        applied.Should().HaveCount(2);
        applied.Should().Contain(orig1);
        applied.Should().Contain(orig2);
        sut.SavedOriginalsForTesting().Should().BeEmpty(); // cleared after restore
    }

    [Fact]
    public void RestoreAll_Skips_Detached_Monitor_Without_Throwing()
    {
        Rect orig = new(0, 0, 1920, 1040);
        var monitorsWith = new[] { M(new IntPtr(1), @"\\.\DISPLAY1", orig) };
        var monitorsWithout = Array.Empty<MonitorDescriptor>();
        var current = monitorsWith;
        var applied = new List<Rect>();
        var sut = new WorkAreaManager(
            () => current,
            r =>
            {
                applied.Add(r);
                return true;
            },
            NullLogger<WorkAreaManager>.Instance
        );

        sut.SetWorkArea(new IntPtr(1), new Rect(160, 0, 1760, 1040));
        applied.Clear();

        // Hot-unplug: enumeration now returns nothing.
        current = monitorsWithout;
        Action act = sut.RestoreAll;
        act.Should().NotThrow();
        applied.Should().BeEmpty();
    }

    [Fact]
    public void RestoreAll_Survives_Hot_Plug_With_Different_Hmonitor_Same_DeviceName()
    {
        Rect orig = new(0, 0, 1920, 1040);
        var before = new[] { M(new IntPtr(1), @"\\.\DISPLAY1", orig) };
        var afterReplug = new[] { M(new IntPtr(42), @"\\.\DISPLAY1", orig) };
        var current = before;
        var applied = new List<Rect>();
        var sut = new WorkAreaManager(
            () => current,
            r =>
            {
                applied.Add(r);
                return true;
            },
            NullLogger<WorkAreaManager>.Instance
        );

        sut.SetWorkArea(new IntPtr(1), new Rect(160, 0, 1760, 1040));
        applied.Clear();

        current = afterReplug;
        sut.RestoreAll();

        applied.Should().ContainSingle().Which.Should().Be(orig);
    }
}
