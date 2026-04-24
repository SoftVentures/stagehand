using System.Diagnostics.CodeAnalysis;
using App.Interop;
using App.Interop.Errors;
using App.Interop.Internal;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Coverage for <see cref="WindowEnumerator"/>. Drives the enumerator through
/// an <see cref="INativeWindowApi"/> substitute so no real HWND is required
/// and failure branches can be scripted deterministically.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class WindowEnumeratorTests
{
    private static readonly IntPtr Hwnd1 = new(0x1001);
    private static readonly IntPtr Hwnd2 = new(0x1002);
    private static readonly IntPtr Hwnd3 = new(0x1003);

    [Fact]
    public void GetManageableWindows_ReturnsAllTopLevelWindows()
    {
        INativeWindowApi api = Substitute.For<INativeWindowApi>();
        api.EnumTopLevel().Returns([Hwnd1, Hwnd2, Hwnd3]);
        ScriptHappyPath(api, Hwnd1, "Notepad", "Notepad", pid: 11, style: 0, exStyle: 0);
        ScriptHappyPath(api, Hwnd2, "Code", "Chrome_WidgetWin_1", pid: 22, style: 0, exStyle: 0);
        ScriptHappyPath(
            api,
            Hwnd3,
            "",
            "Progman",
            pid: 33,
            style: 0x10000000L,
            exStyle: 0x00000080L
        );

        var processNames = new Dictionary<int, string?>
        {
            [11] = "notepad",
            [22] = "Code",
            [33] = "explorer",
        };

        var enumerator = new WindowEnumerator(
            api,
            pid => processNames[pid],
            NullLogger<WindowEnumerator>.Instance
        );

        IReadOnlyList<WindowSnapshot> result = enumerator.GetManageableWindows();

        result.Should().HaveCount(3);
        result[0].Hwnd.Should().Be(Hwnd1);
        result[0].Title.Should().Be("Notepad");
        result[0].ClassName.Should().Be("Notepad");
        result[0].ProcessId.Should().Be(11);
        result[0].ProcessName.Should().Be("notepad");
        result[0].IsVisible.Should().BeTrue();
        result[0].IsTopLevel.Should().BeTrue();
        result[0].IsCloaked.Should().BeFalse();
        result[0].HasOwner.Should().BeFalse();

        result[1].Title.Should().Be("Code");
        result[1].ClassName.Should().Be("Chrome_WidgetWin_1");
        result[1].ProcessName.Should().Be("Code");

        result[2].Title.Should().BeEmpty();
        result[2].ClassName.Should().Be("Progman");
        result[2].ExStyle.Should().Be(0x00000080L);
    }

    [Fact]
    public void PerWindowException_IsSwallowed_AndLoggedDebug()
    {
        INativeWindowApi api = Substitute.For<INativeWindowApi>();
        api.EnumTopLevel().Returns([Hwnd1, Hwnd2, Hwnd3]);
        ScriptHappyPath(api, Hwnd1, "A", "ClassA", pid: 1, style: 0, exStyle: 0);
        ScriptHappyPath(api, Hwnd3, "C", "ClassC", pid: 3, style: 0, exStyle: 0);
        // Hwnd2's GetWindowText blows up — mid-enumeration close / stale HWND.
        api.GetAncestorRoot(Hwnd2).Returns(Hwnd2);
        api.IsWindowVisible(Hwnd2).Returns(true);
        api.IsCloaked(Hwnd2).Returns(false);
        api.GetWindowText(Hwnd2)
            .Returns(_ => throw new Win32InteropException(1400, "Invalid window handle"));

        ILogger<WindowEnumerator> log = Substitute.For<ILogger<WindowEnumerator>>();
        log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var enumerator = new WindowEnumerator(
            api,
            pid =>
                pid == 1 ? "a"
                : pid == 3 ? "c"
                : "ghost",
            log
        );

        IReadOnlyList<WindowSnapshot> result = enumerator.GetManageableWindows();

        result.Should().HaveCount(2, "the failing window must be skipped, not crash the pass");
        result.Select(s => s.Hwnd).Should().BeEquivalentTo(new[] { Hwnd1, Hwnd3 });

        // Verify the skip was logged at Debug with the offending HWND in the message.
        log.Received(1)
            .Log(
                LogLevel.Debug,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o!.ToString()!.Contains("4098", StringComparison.Ordinal)),
                Arg.Is<Win32InteropException>(ex => ex.Win32ErrorCode == 1400),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public void StableIdentity_SameHwnd_ReturnsSameProcessIdentity()
    {
        INativeWindowApi api = Substitute.For<INativeWindowApi>();
        api.EnumTopLevel().Returns([Hwnd1]);
        ScriptHappyPath(
            api,
            Hwnd1,
            "Stable",
            "StableClass",
            pid: 42,
            style: 0,
            exStyle: 0,
            startTicks: 638000000000000000L
        );

        var enumerator = new WindowEnumerator(
            api,
            pid => "stable.exe",
            NullLogger<WindowEnumerator>.Instance
        );

        IReadOnlyList<WindowSnapshot> first = enumerator.GetManageableWindows();
        IReadOnlyList<WindowSnapshot> second = enumerator.GetManageableWindows();

        first.Should().HaveCount(1);
        second.Should().HaveCount(1);

        WindowSnapshot a = first[0];
        WindowSnapshot b = second[0];

        (a.Hwnd, a.ProcessId, a.ProcessStartTimeUtcTicks)
            .Should()
            .Be(
                (b.Hwnd, b.ProcessId, b.ProcessStartTimeUtcTicks),
                "the three-field stable identity tuple must be bit-identical for the same live HWND"
            );
    }

    /// <summary>
    /// Scripts <paramref name="api"/> so every per-HWND method returns a
    /// sensible default for <paramref name="hwnd"/>. Tests that want to
    /// exercise a specific failure branch override the relevant method AFTER
    /// calling this helper.
    /// </summary>
    private static void ScriptHappyPath(
        INativeWindowApi api,
        IntPtr hwnd,
        string title,
        string className,
        int pid,
        long style,
        long exStyle,
        long startTicks = 638123456789012345L
    )
    {
        api.GetAncestorRoot(hwnd).Returns(hwnd);
        api.IsWindowVisible(hwnd).Returns(true);
        api.IsCloaked(hwnd).Returns(false);
        api.GetWindowText(hwnd).Returns(title);
        api.GetClassName(hwnd).Returns(className);
        api.GetWindowStyle(hwnd).Returns(style);
        api.GetWindowExStyle(hwnd).Returns(exStyle);
        api.GetWindowOwner(hwnd).Returns(IntPtr.Zero);
        api.GetWindowRect(hwnd).Returns(new Rect(0, 0, 800, 600));
        api.GetProcessId(hwnd).Returns(pid);
        api.GetProcessStartTimeUtcTicks(pid).Returns(startTicks);
        api.MonitorFromWindow(hwnd).Returns(new IntPtr(1));
    }
}
