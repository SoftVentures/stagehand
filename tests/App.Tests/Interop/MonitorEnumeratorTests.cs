using System.Diagnostics.CodeAnalysis;
using App.Interop;
using FluentAssertions;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Smoke tests for <see cref="MonitorEnumerator"/> against the real desktop
/// of the test host. CI runners always have a primary monitor (the
/// virtual display the test agent runs in), so the assertions here are
/// satisfiable everywhere.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class MonitorEnumeratorTests
{
    [Fact]
    public void EnumerateAll_Returns_At_Least_One_Monitor()
    {
        var sut = new MonitorEnumerator();
        IReadOnlyList<MonitorDescriptor> monitors = sut.EnumerateAll();
        monitors.Should().NotBeEmpty();
    }

    [Fact]
    public void GetPrimary_Returns_A_Monitor_With_IsPrimary_True()
    {
        var sut = new MonitorEnumerator();
        MonitorDescriptor? primary = sut.GetPrimary();
        primary.Should().NotBeNull();
        primary!.Value.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void EnumerateAll_Contains_The_Primary_Monitor()
    {
        var sut = new MonitorEnumerator();
        IReadOnlyList<MonitorDescriptor> all = sut.EnumerateAll();
        all.Should().Contain(m => m.IsPrimary);
    }

    [Fact]
    public void Enumerated_DeviceNames_Are_Unique()
    {
        var sut = new MonitorEnumerator();
        IReadOnlyList<MonitorDescriptor> all = sut.EnumerateAll();
        all.Select(m => m.DeviceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should()
            .HaveCount(all.Count);
    }

    [Fact]
    public void ResolveByDeviceName_Roundtrips_Primary()
    {
        var sut = new MonitorEnumerator();
        MonitorDescriptor primary = sut.GetPrimary()!.Value;
        MonitorDescriptor? roundtrip = sut.ResolveByDeviceName(primary.DeviceName);
        roundtrip.Should().NotBeNull();
        roundtrip!.Value.DeviceName.Should().Be(primary.DeviceName);
    }

    [Fact]
    public void ResolveByDeviceName_Returns_Null_For_Unknown()
    {
        var sut = new MonitorEnumerator();
        MonitorDescriptor? unknown = sut.ResolveByDeviceName(@"\\.\DISPLAY999");
        unknown.Should().BeNull();
    }
}
