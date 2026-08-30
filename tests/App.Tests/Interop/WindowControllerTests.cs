using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using App.Interop;
using App.Interop.Errors;
using App.Interop.Internal;
using App.Interop.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behavioural coverage for <see cref="WindowController"/>. The
/// <see cref="INativeWindowApi"/> seam is substituted with
/// <see cref="NSubstitute"/>, letting every test script the exact return
/// values and error codes that the controller is expected to react to —
/// including the elevation-boundary branch and the cross-thread
/// <c>AttachThreadInput</c> dance — without touching a real HWND.
/// </summary>
/// <remarks>
/// <para>
/// For <c>GetLastError</c> we deliberately chose the minimally-invasive route
/// of adding a <see cref="INativeWindowApi.GetLastErrorCode"/> method on the
/// seam instead of changing <see cref="INativeWindowApi.SetWindowPos"/>'s
/// return shape to a tuple. Win32's real model is identical
/// (<c>GetLastError</c> is a thread-local function the caller invokes after a
/// failing call), and this keeps the existing <c>bool</c> signature — and all
/// Wave 1 tests that depend on it — untouched.
/// </para>
/// <para>
/// <see cref="UiDispatcher.AssertOnUiThread"/> is debug-only and relies on WPF
/// dispatcher affinity. The tests construct a real <see cref="UiDispatcher"/>
/// bound to <see cref="Dispatcher.CurrentDispatcher"/> so the assertion
/// passes; in Release the call is a no-op regardless.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class WindowControllerTests
{
    private static readonly IntPtr TestHwnd = new(0x1234);

    // -------------------------------------------------------------------
    // Park
    // -------------------------------------------------------------------

    [Fact]
    public void Park_CallsSetWindowPos_WithCorrectFlags()
    {
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);
        var originalBounds = new Rect(100, 200, 800, 600);

        controller.Park(TestHwnd, originalBounds);

        api.Received(1)
            .SetWindowPos(
                TestHwnd,
                Arg.Is<Rect>(r =>
                    r.X == -32000 && r.Y == -32000 && r.Width == 800 && r.Height == 600
                ),
                SetWindowPosFlags.NoZOrder
                    | SetWindowPosFlags.NoActivate
                    | SetWindowPosFlags.NoRedraw
            );
    }

    [Fact]
    public void Park_ElevatedWindow_ThrowsElevationBoundaryException()
    {
        // Windows denies SetWindowPos on a higher-integrity window:
        // SetWindowPos returns false, GetLastError == 5 (ERROR_ACCESS_DENIED).
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(false);
        api.GetLastErrorCode().Returns(5);

        Action act = () => controller.Park(TestHwnd, new Rect(0, 0, 100, 100));

        act.Should()
            .Throw<ElevationBoundaryException>()
            .Which.Hwnd.Should()
            .Be(TestHwnd, "the thrown exception must carry the offending HWND for caller logging");
    }

    [Fact]
    public void Park_NonElevationFailure_ThrowsWin32InteropException()
    {
        // Any other Win32 error → generic Win32InteropException, not elevation.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(false);
        api.GetLastErrorCode().Returns(1400); // ERROR_INVALID_WINDOW_HANDLE

        Action act = () => controller.Park(TestHwnd, new Rect(0, 0, 100, 100));

        act.Should().Throw<Win32InteropException>().Which.Win32ErrorCode.Should().Be(1400);
    }

    // -------------------------------------------------------------------
    // RestorePosition
    // -------------------------------------------------------------------

    [Fact]
    public void RestorePosition_UsesPassedRect()
    {
        // Restore must NOT pass NoRedraw — without a redraw burst the target
        // region stays un-painted on real hardware (UWP cloak bug repro,
        // Plan 02 §Design.4 fix). The assertion checks both the bits that
        // must be set and that NoRedraw is NOT set.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);
        var bounds = new Rect(50, 60, 1024, 768);

        controller.RestorePosition(TestHwnd, bounds);

        api.Received(1)
            .SetWindowPos(
                TestHwnd,
                Arg.Is<Rect>(r => r.X == 50 && r.Y == 60 && r.Width == 1024 && r.Height == 768),
                Arg.Is<SetWindowPosFlags>(f =>
                    f == (SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate)
                )
            );
    }

    [Fact]
    public void RestorePosition_WhenCloaked_CallsShowWindow_With_SW_SHOWNA()
    {
        // UWP / minimize-to-tray windows often have DWMWA_CLOAKED set after
        // being parked off-screen. RestorePosition must clear the cloak via
        // ShowWindow(SW_SHOWNA) — SW_SHOWNA is 8 and asserts visibility
        // without stealing focus.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);
        api.IsCloaked(TestHwnd).Returns(true);
        api.ShowWindow(Arg.Any<IntPtr>(), Arg.Any<int>()).Returns(false);

        controller.RestorePosition(TestHwnd, new Rect(0, 0, 100, 100));

        const int SwShowna = 8;
        api.Received(1).ShowWindow(TestHwnd, SwShowna);
    }

    [Fact]
    public void RestorePosition_WhenNotCloaked_DoesNotCallShowWindow()
    {
        // Normal Win32 windows (Notepad, Explorer, etc.) are not cloaked at
        // park time. Avoid the redundant ShowWindow call so we don't
        // accidentally toggle visibility on apps that intentionally hid
        // themselves.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);
        api.IsCloaked(TestHwnd).Returns(false);

        controller.RestorePosition(TestHwnd, new Rect(0, 0, 100, 100));

        api.DidNotReceive().ShowWindow(Arg.Any<IntPtr>(), Arg.Any<int>());
    }

    // -------------------------------------------------------------------
    // Resize
    // -------------------------------------------------------------------

    [Fact]
    public void Resize_UsesFlags_NoZOrder_NoActivate()
    {
        // Crucial: Resize must NOT pass NoRedraw — the active window has to
        // repaint at the new size. The assertion therefore checks both the
        // bits that must be set *and* that NoRedraw is not set.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);
        var bounds = new Rect(10, 20, 1600, 900);

        controller.Resize(TestHwnd, bounds);

        api.Received(1)
            .SetWindowPos(
                TestHwnd,
                Arg.Is<Rect>(r => r.X == 10 && r.Y == 20 && r.Width == 1600 && r.Height == 900),
                Arg.Is<SetWindowPosFlags>(f =>
                    f == (SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate)
                )
            );
    }

    [Fact]
    public void Resize_RestoresMaximisedWindowBeforeSettingPos()
    {
        // Regression: pre-Plan-03-fix, maximised windows ignored the new
        // bounds because WINDOWPLACEMENT.showCmd stayed SW_MAXIMIZE.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.IsZoomed(TestHwnd).Returns(true);
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);

        controller.Resize(TestHwnd, new Rect(0, 0, 800, 600));

        Received.InOrder(() =>
        {
            api.ShowWindow(
                TestHwnd,
                9 /* SW_RESTORE */
            );
            api.SetWindowPos(TestHwnd, Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>());
        });
    }

    [Fact]
    public void Resize_NonMaximisedWindow_DoesNotCallShowWindow()
    {
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.IsZoomed(TestHwnd).Returns(false);
        api.SetWindowPos(Arg.Any<IntPtr>(), Arg.Any<Rect>(), Arg.Any<SetWindowPosFlags>())
            .Returns(true);

        controller.Resize(TestHwnd, new Rect(0, 0, 800, 600));

        api.DidNotReceive().ShowWindow(TestHwnd, Arg.Any<int>());
    }

    // -------------------------------------------------------------------
    // BringToFront
    // -------------------------------------------------------------------

    [Fact]
    public void BringToFront_AlreadyForeground_IsNoOp()
    {
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(TestHwnd);

        controller.BringToFront(TestHwnd);

        // No thread IDs queried, no SetForegroundWindow, no AttachThreadInput.
        api.DidNotReceive().GetCurrentThreadId();
        api.DidNotReceive().SetForegroundWindow(Arg.Any<IntPtr>());
        api.DidNotReceive().AttachThreadInput(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<bool>());
    }

    [Fact]
    public void BringToFront_RestoresMinimisedWindow()
    {
        // Regression: clicking a tile for a minimised window must restore
        // it before SetForegroundWindow — otherwise the OS marks the iconic
        // HWND foreground and the user sees the desktop instead of the app.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.IsIconic(TestHwnd).Returns(true);
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD));
        api.GetCurrentThreadId().Returns(42u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(42u);
        api.SetForegroundWindow(TestHwnd).Returns(true);

        controller.BringToFront(TestHwnd);

        Received.InOrder(() =>
        {
            api.ShowWindow(
                TestHwnd,
                9 /* SW_RESTORE */
            );
            _ = api.SetForegroundWindow(TestHwnd);
        });
    }

    [Fact]
    public void BringToFront_HappyPath_SameThread()
    {
        // Target and caller live on the same thread → no AttachThreadInput.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD)); // different from TestHwnd
        api.GetCurrentThreadId().Returns(42u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(42u);
        api.SetForegroundWindow(TestHwnd).Returns(true);

        controller.BringToFront(TestHwnd);

        Received.InOrder(() =>
        {
            _ = api.GetForegroundWindow();
            _ = api.GetCurrentThreadId();
            _ = api.GetWindowThreadProcessId(TestHwnd, out var _);
            _ = api.SetForegroundWindow(TestHwnd);
        });
        api.DidNotReceive().AttachThreadInput(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<bool>());
    }

    [Fact]
    public void BringToFront_AttachPath_DifferentThread()
    {
        // Different threads → AttachThreadInput(true), SetForegroundWindow,
        // AttachThreadInput(false) in finally. Order matters.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD));
        api.GetCurrentThreadId().Returns(100u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(200u);
        api.SetForegroundWindow(TestHwnd).Returns(true);
        api.AttachThreadInput(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<bool>()).Returns(true);

        controller.BringToFront(TestHwnd);

        Received.InOrder(() =>
        {
            _ = api.GetForegroundWindow();
            _ = api.GetCurrentThreadId();
            _ = api.GetWindowThreadProcessId(TestHwnd, out var _);
            _ = api.AttachThreadInput(100u, 200u, true);
            _ = api.SetForegroundWindow(TestHwnd);
            _ = api.AttachThreadInput(100u, 200u, false);
        });
    }

    [Fact]
    public void BringToFront_AttachPath_DetachesEvenWhenSetForegroundWindowThrows()
    {
        // If SetForegroundWindow blows up unexpectedly, the detach MUST still
        // happen — otherwise the target thread stays input-attached and its
        // foreground queue is hung. try/finally is load-bearing.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD));
        api.GetCurrentThreadId().Returns(100u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(200u);
        api.AttachThreadInput(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<bool>()).Returns(true);
        api.SetForegroundWindow(TestHwnd).Returns(_ => throw new InvalidOperationException("boom"));

        Action act = () => controller.BringToFront(TestHwnd);

        act.Should().Throw<InvalidOperationException>();
        api.Received(1).AttachThreadInput(100u, 200u, false);
    }

    [Fact]
    public void BringToFront_Failure_ThrowsWin32InteropException()
    {
        // SetForegroundWindow still returns false after the dance →
        // Win32InteropException with the captured error code.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD));
        api.GetCurrentThreadId().Returns(100u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(200u);
        api.AttachThreadInput(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<bool>()).Returns(true);
        api.SetForegroundWindow(TestHwnd).Returns(false);
        api.GetLastErrorCode().Returns(0);

        Action act = () => controller.BringToFront(TestHwnd);

        act.Should().Throw<Win32InteropException>();
        // Detach still happened despite the failure.
        api.Received(1).AttachThreadInput(100u, 200u, false);
    }

    [Fact]
    public void BringToFront_AttachFailure_IsLoggedAndSwallowed_NoForegroundCall()
    {
        // AttachThreadInput(attach:true) returns false → the controller logs
        // a warning and skips the SetForegroundWindow call entirely. Because
        // the attach never succeeded, no detach is necessary either.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD));
        api.GetCurrentThreadId().Returns(100u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(200u);
        api.AttachThreadInput(100u, 200u, true).Returns(false);
        api.GetLastErrorCode().Returns(87); // ERROR_INVALID_PARAMETER — arbitrary

        Action act = () => controller.BringToFront(TestHwnd);

        act.Should().NotThrow();
        api.DidNotReceive().SetForegroundWindow(Arg.Any<IntPtr>());
        api.DidNotReceive().AttachThreadInput(Arg.Any<uint>(), Arg.Any<uint>(), false);
    }

    [Fact]
    public void BringToFront_DetachReturnsFalse_DoesNotThrow()
    {
        // Contract: AttachThreadInput returns bool and never throws. A
        // failing detach in the `finally` must not propagate — the happy-path
        // call should complete normally.
        (INativeWindowApi api, WindowController controller) = BuildController();
        api.GetForegroundWindow().Returns(new IntPtr(0xDEAD));
        api.GetCurrentThreadId().Returns(100u);
        api.GetWindowThreadProcessId(TestHwnd, out var _).Returns(200u);
        api.AttachThreadInput(100u, 200u, true).Returns(true);
        api.AttachThreadInput(100u, 200u, false).Returns(false);
        api.SetForegroundWindow(TestHwnd).Returns(true);

        Action act = () => controller.BringToFront(TestHwnd);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="WindowController"/> wired to an NSubstitute
    /// <see cref="INativeWindowApi"/>, using a real <see cref="UiDispatcher"/>
    /// bound to the current test thread so <c>AssertOnUiThread</c> passes.
    /// </summary>
    private static (INativeWindowApi Api, WindowController Controller) BuildController()
    {
        INativeWindowApi api = Substitute.For<INativeWindowApi>();
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var ui = new UiDispatcher(dispatcher);
        var controller = new WindowController(api, ui, NullLogger<WindowController>.Instance);
        return (api, controller);
    }
}
