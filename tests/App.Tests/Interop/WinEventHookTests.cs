using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using App.Interop;
using App.Interop.Internal;
using App.Interop.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behaviour tests for <see cref="WinEventHook"/>. Drives the internal
/// callback entry point directly through
/// <c>InvokeCallbackForTesting</c> so we can exercise the coalescing /
/// marshalling path without involving real Win32 hooks.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class WinEventHookTests
{
    [Fact]
    public void Constructor_Installs_Hook_Via_Native_Seam_With_Spec_Values()
    {
        (INativeEventApi native, SafeWinEventHookHandle handle) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        UiDispatcher ui = BuildUiDispatcher();

        var spec = new WinEventSpec(
            EventMin: 0x8000,
            EventMax: 0x8001,
            Flags: WinEventFlags.OutOfContext,
            IdProcess: 1234,
            IdThread: 0,
            CoalesceWindow: null
        );

        using var hook = new WinEventHook(
            spec,
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        native
            .Received(1)
            .SetWinEventHook(
                0x8000,
                0x8001,
                Arg.Any<NativeMethods.WinEventDelegate>(),
                1234,
                0,
                0x0000
            );
        handle.IsClosed.Should().BeFalse("install should produce a live handle");
    }

    [Fact]
    public void Fired_Event_IsMarshalled_To_UI_Thread()
    {
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var ui = new UiDispatcher(dispatcher);

        using var hook = new WinEventHook(
            new WinEventSpec(EventMin: 0x0003, EventMax: 0x0003),
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        var captured = new List<WinEventArgs>();
        hook.Fired += (_, args) => captured.Add(args);

        hook.InvokeCallbackForTesting(0x0003, new IntPtr(0xAA), 0, 0, 100, 999);

        // UiDispatcher.Post uses BeginInvoke — drain the frame explicitly so
        // the handler runs before we assert.
        DrainDispatcherFrames(dispatcher);

        captured.Should().ContainSingle();
        captured[0].EventId.Should().Be(0x0003u);
        captured[0].Hwnd.Should().Be(new IntPtr(0xAA));
        captured[0].Thread.Should().Be(100u);
        captured[0].Time.Should().Be(999u);
    }

    [Fact]
    public void Coalescing_Same_EventId_And_Hwnd_IsDeduped_Within_Window()
    {
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var ui = new UiDispatcher(dispatcher);

        using var hook = new WinEventHook(
            new WinEventSpec(
                EventMin: 0x800B,
                EventMax: 0x800B,
                CoalesceWindow: TimeSpan.FromSeconds(10)
            ),
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        var captured = new List<WinEventArgs>();
        hook.Fired += (_, args) => captured.Add(args);

        var hwnd = new IntPtr(0x1234);
        for (var i = 0; i < 10; i++)
        {
            hook.InvokeCallbackForTesting(0x800B, hwnd);
        }

        DrainDispatcherFrames(dispatcher);

        captured
            .Should()
            .ContainSingle(
                "repeated (EventId, Hwnd) within the coalesce window collapse to one forward"
            );
    }

    [Fact]
    public void Coalescing_Different_Hwnd_Or_EventId_IsNotDeduped()
    {
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var ui = new UiDispatcher(dispatcher);

        using var hook = new WinEventHook(
            new WinEventSpec(
                EventMin: 0x0001,
                EventMax: 0xFFFF,
                CoalesceWindow: TimeSpan.FromSeconds(10)
            ),
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        var captured = new List<WinEventArgs>();
        hook.Fired += (_, args) => captured.Add(args);

        hook.InvokeCallbackForTesting(0x0003, new IntPtr(0xAA));
        hook.InvokeCallbackForTesting(0x0003, new IntPtr(0xBB)); // different hwnd
        hook.InvokeCallbackForTesting(0x8001, new IntPtr(0xAA)); // different event id

        DrainDispatcherFrames(dispatcher);

        captured.Should().HaveCount(3, "distinct (EventId, Hwnd) pairs are independent");
    }

    [Fact]
    public void Two_Instances_Have_Independent_Coalescing()
    {
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var ui = new UiDispatcher(dispatcher);

        var spec = new WinEventSpec(
            EventMin: 0x0003,
            EventMax: 0x0003,
            CoalesceWindow: TimeSpan.FromSeconds(10)
        );

        using var hookA = new WinEventHook(
            spec,
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );
        using var hookB = new WinEventHook(
            spec,
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        var capturedA = 0;
        var capturedB = 0;
        hookA.Fired += (_, _) => capturedA++;
        hookB.Fired += (_, _) => capturedB++;

        var hwnd = new IntPtr(0x1234);
        // Hit A three times — A should coalesce to 1.
        hookA.InvokeCallbackForTesting(0x0003, hwnd);
        hookA.InvokeCallbackForTesting(0x0003, hwnd);
        hookA.InvokeCallbackForTesting(0x0003, hwnd);
        // Hit B once — B has never seen this key, so it must fire even
        // though the "same" event range was already consumed by A.
        hookB.InvokeCallbackForTesting(0x0003, hwnd);

        DrainDispatcherFrames(dispatcher);

        capturedA.Should().Be(1, "A coalesces its own three bursts");
        capturedB.Should().Be(1, "B has its own ring and must still fire");
    }

    [Fact]
    public void Dispose_After_Thread_Shutdown_LogsWarning_DoesNotThrow()
    {
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        UiDispatcher ui = BuildUiDispatcher();
        ILogger<WinEventHook> log = Substitute.For<ILogger<WinEventHook>>();
        log.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var hook = new WinEventHook(
            new WinEventSpec(EventMin: 0x0003, EventMax: 0x0003),
            ui,
            thread,
            log,
            native
        );

        // Stop the hook thread out from under the hook.
        thread.Dispose();

        Action action = () => hook.Dispose();
        action.Should().NotThrow("Dispose after thread shutdown must swallow and log");

        log.Received()
            .Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public void Flags_Enum_IsPassedThrough_ToNative_As_UInt()
    {
        // WinEventFlags is a [Flags] enum backed by uint; the wrapper must
        // cast to the raw value expected by SetWinEventHook.
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        UiDispatcher ui = BuildUiDispatcher();

        using var _ = new WinEventHook(
            new WinEventSpec(
                EventMin: 0x0003,
                EventMax: 0x0003,
                Flags: WinEventFlags.OutOfContext | WinEventFlags.SkipOwnProcess
            ),
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        native
            .Received(1)
            .SetWinEventHook(
                0x0003,
                0x0003,
                Arg.Any<NativeMethods.WinEventDelegate>(),
                Arg.Any<uint>(),
                Arg.Any<uint>(),
                0x0002u /* WINEVENT_SKIPOWNPROCESS == OutOfContext (0) | SkipOwnProcess (2) */
            );
    }

    [Fact]
    public void Selectivity_IdProcess_Is_Passed_Through_To_Native_SetWinEventHook()
    {
        (INativeEventApi native, SafeWinEventHookHandle _) = BuildFakeNativeApi();
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        UiDispatcher ui = BuildUiDispatcher();

        using var _ = new WinEventHook(
            new WinEventSpec(EventMin: 0x800B, EventMax: 0x800B, IdProcess: 4242, IdThread: 7),
            ui,
            thread,
            NullLogger<WinEventHook>.Instance,
            native
        );

        native
            .Received(1)
            .SetWinEventHook(
                0x800B,
                0x800B,
                Arg.Any<NativeMethods.WinEventDelegate>(),
                4242,
                7,
                Arg.Any<uint>()
            );
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static (INativeEventApi Native, SafeWinEventHookHandle Handle) BuildFakeNativeApi()
    {
        // ownsHandle:false keeps SafeHandle.ReleaseHandle from calling into
        // real user32 when the hook is disposed. We're asserting behaviour of
        // the wrapper, not the OS.
        var handle = new SafeWinEventHookHandle(new IntPtr(0xABCD), ownsHandle: false);
        INativeEventApi native = Substitute.For<INativeEventApi>();
        native
            .SetWinEventHook(
                Arg.Any<uint>(),
                Arg.Any<uint>(),
                Arg.Any<NativeMethods.WinEventDelegate>(),
                Arg.Any<uint>(),
                Arg.Any<uint>(),
                Arg.Any<uint>()
            )
            .Returns(handle);
        return (native, handle);
    }

    private static UiDispatcher BuildUiDispatcher() => new(Dispatcher.CurrentDispatcher);

    /// <summary>
    /// Pumps the current-thread dispatcher until all queued Background frames
    /// drain, so <see cref="UiDispatcher.Post"/> continuations run before the
    /// test assertions.
    /// </summary>
    private static void DrainDispatcherFrames(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false)
        );
        Dispatcher.PushFrame(frame);
    }
}
