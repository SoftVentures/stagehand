using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Services.Windows;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Windows;

/// <summary>
/// Coverage for <see cref="ManageableWindowService"/>: asserts that the raw
/// enumerator output is passed through the injected <see cref="IWindowFilter"/>
/// and only accepted snapshots are returned.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class ManageableWindowServiceTests
{
    [Fact]
    public void GetManageableWindows_OnlyReturnsSnapshotsAcceptedByFilter()
    {
        WindowSnapshot snap1 = BuildSnapshot(new IntPtr(0x1), "keep1", "ClassA");
        WindowSnapshot snap2 = BuildSnapshot(new IntPtr(0x2), "drop", "ClassB");
        WindowSnapshot snap3 = BuildSnapshot(new IntPtr(0x3), "keep2", "ClassC");

        IWindowEnumerator enumerator = Substitute.For<IWindowEnumerator>();
        enumerator.GetManageableWindows().Returns([snap1, snap2, snap3]);

        IWindowFilter filter = Substitute.For<IWindowFilter>();
        filter.IsManageable(snap1).Returns(true);
        filter.IsManageable(snap2).Returns(false);
        filter.IsManageable(snap3).Returns(true);

        var service = new ManageableWindowService(
            enumerator,
            filter,
            NullLogger<ManageableWindowService>.Instance
        );

        IReadOnlyList<WindowSnapshot> result = service.GetManageableWindows();

        result.Should().HaveCount(2);
        result.Select(s => s.Hwnd).Should().BeEquivalentTo(new[] { snap1.Hwnd, snap3.Hwnd });
        filter.Received(3).IsManageable(Arg.Any<WindowSnapshot>());
    }

    [Fact]
    public void GetManageableWindows_ReturnsEmpty_WhenEnumeratorReturnsEmpty()
    {
        IWindowEnumerator enumerator = Substitute.For<IWindowEnumerator>();
        enumerator.GetManageableWindows().Returns([]);
        IWindowFilter filter = Substitute.For<IWindowFilter>();

        var service = new ManageableWindowService(
            enumerator,
            filter,
            NullLogger<ManageableWindowService>.Instance
        );

        service.GetManageableWindows().Should().BeEmpty();
        filter.DidNotReceive().IsManageable(Arg.Any<WindowSnapshot>());
    }

    private static WindowSnapshot BuildSnapshot(IntPtr hwnd, string title, string className) =>
        new(
            Hwnd: hwnd,
            Title: title,
            ClassName: className,
            ProcessId: (int)(long)hwnd,
            ProcessStartTimeUtcTicks: 638000000000000000L,
            Bounds: new Rect(0, 0, 400, 300),
            Monitor: new IntPtr(1),
            IsVisible: true,
            IsCloaked: false,
            IsTopLevel: true,
            Style: 0L,
            ExStyle: 0L,
            HasOwner: false,
            ProcessName: "test"
        );
}
