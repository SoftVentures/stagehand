using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using App.Interop;
using App.Interop.Internal;
using App.Interop.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Lifecycle coverage for <see cref="DwmThumbnail"/> and
/// <see cref="DwmThumbnailFactory"/>. Wraps the <see cref="INativeDwmApi"/>
/// seam in an NSubstitute fake so we can assert register/unregister symmetry
/// across thousands of iterations without a real DWM.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class DwmThumbnailLifecycleTests
{
    private const int Iterations = 1000;

    [Fact]
    public void Register_And_Dispose_Issue_Exactly_One_Register_And_One_Unregister_Per_Instance()
    {
        (
            INativeDwmApi native,
            Counter registerCalls,
            Counter unregisterCalls,
            DwmThumbnailFactory factory
        ) = BuildFactoryAndCounters();

        for (var i = 0; i < Iterations; i++)
        {
            DwmThumbnail thumbnail = factory.Register(
                sourceHwnd: new IntPtr(0x1000 + i),
                destinationHwnd: new IntPtr(0x2000)
            );
            thumbnail.Dispose();
        }

        registerCalls
            .Value.Should()
            .Be(Iterations, "each Register() must hit the native seam exactly once");
        unregisterCalls
            .Value.Should()
            .Be(Iterations, "each Dispose() must unregister exactly once");
        native.Received(Iterations).DwmRegisterThumbnail(Arg.Any<IntPtr>(), Arg.Any<IntPtr>());
        native.Received(Iterations).DwmUnregisterThumbnail(Arg.Any<IntPtr>());
    }

    [Fact]
    public void Dispose_Is_Idempotent_Second_Call_Does_Not_Re_Unregister()
    {
        (INativeDwmApi native, Counter _, Counter unregisterCalls, DwmThumbnailFactory factory) =
            BuildFactoryAndCounters();

        DwmThumbnail t = factory.Register(new IntPtr(0x1), new IntPtr(0x2));
        t.Dispose();
        Action second = () => t.Dispose();

        second.Should().NotThrow("double-dispose must be a no-op");
        unregisterCalls.Value.Should().Be(1, "the second dispose must not re-release the handle");
        native.Received(1).DwmUnregisterThumbnail(Arg.Any<IntPtr>());
    }

    private static (
        INativeDwmApi Native,
        Counter Register,
        Counter Unregister,
        DwmThumbnailFactory Factory
    ) BuildFactoryAndCounters()
    {
        INativeDwmApi native = Substitute.For<INativeDwmApi>();
        var register = new Counter();
        var unregister = new Counter();

        // Hand out a distinct SafeHandle per registration so dispose dispatches
        // to a unique raw pointer — lets us assert one-to-one symmetry instead
        // of a single accidental release covering all handles. ownsHandle:
        // false keeps SafeHandle.ReleaseHandle (which would call into the real
        // dwmapi.dll) out of the test; DwmThumbnail.Dispose routes the release
        // through the INativeDwmApi seam which we observe below.
        native
            .DwmRegisterThumbnail(Arg.Any<IntPtr>(), Arg.Any<IntPtr>())
            .Returns(_ =>
            {
                register.Value++;
                return new SafeDwmThumbnailHandle(new IntPtr(register.Value), ownsHandle: false);
            });

        native.When(x => x.DwmUnregisterThumbnail(Arg.Any<IntPtr>())).Do(_ => unregister.Value++);

        // Stand up a real WPF Dispatcher on the test thread so
        // UiDispatcher.AssertOnUiThread (Debug-only) passes.
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var ui = new UiDispatcher(dispatcher);
        var factory = new DwmThumbnailFactory(native, ui, NullLoggerFactory.Instance);
        return (native, register, unregister, factory);
    }

    private sealed class Counter
    {
        public int Value;
    }
}
