using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Interop.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Stage;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class InstantSceneSwapExecutorTests
{
    private static SceneSwapPlan BuildPlan(
        IReadOnlyList<SceneWindowMove> incoming,
        IReadOnlyList<SceneWindowMove> outgoing,
        WindowIdentity primary
    ) =>
        new(
            IncomingSceneId: SceneId.New(),
            IncomingMoves: incoming,
            OutgoingSceneId: SceneId.New(),
            OutgoingMoves: outgoing,
            TargetDeviceName: @"\\.\DISPLAY1",
            IncomingPrimary: primary
        );

    [Fact]
    public async Task RunAsync_Resizes_Outgoing_Then_Incoming_Then_BringsToFront()
    {
        var windows = Substitute.For<IWindowController>();
        var sut = new InstantSceneSwapExecutor(
            windows,
            NullLogger<InstantSceneSwapExecutor>.Instance
        );

        var primary = new WindowIdentity(new IntPtr(0xA), 100, 0);
        var incoming = new SceneWindowMove[]
        {
            new(primary, new Rect(0, 0, 100, 100), new Rect(200, 0, 1700, 1080)),
        };
        var outgoing = new SceneWindowMove[]
        {
            new(
                new WindowIdentity(new IntPtr(0xB), 200, 0),
                new Rect(200, 0, 1700, 1080),
                new Rect(-32000, -32000, 1, 1)
            ),
        };

        await sut.RunAsync(
                BuildPlan(incoming, outgoing, primary),
                AnimationSpeed.Off,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        // Order: incoming-resize, BringToFront(incoming), THEN outgoing-park.
        // Parking outgoing first creates a foreground-vacuum that races the
        // WinEvent hook into an infinite swap loop — see executor source.
        Received.InOrder(() =>
        {
            windows.Resize(new IntPtr(0xA), Arg.Any<Rect>());
            windows.BringToFront(new IntPtr(0xA));
            windows.Resize(new IntPtr(0xB), Arg.Any<Rect>());
        });
    }

    [Fact]
    public async Task RunAsync_Continues_When_Resize_Fails_For_One_Window()
    {
        var windows = Substitute.For<IWindowController>();
        windows
            .When(w => w.Resize(new IntPtr(0xB), Arg.Any<Rect>()))
            .Do(_ => throw new Win32InteropException(0, "boom"));
        var sut = new InstantSceneSwapExecutor(
            windows,
            NullLogger<InstantSceneSwapExecutor>.Instance
        );

        var primary = new WindowIdentity(new IntPtr(0xA), 100, 0);
        var incoming = new SceneWindowMove[]
        {
            new(primary, new Rect(0, 0, 100, 100), new Rect(200, 0, 1700, 1080)),
        };
        var outgoing = new SceneWindowMove[]
        {
            new(
                new WindowIdentity(new IntPtr(0xB), 200, 0),
                new Rect(200, 0, 1700, 1080),
                new Rect(-32000, -32000, 1, 1)
            ),
        };

        Func<Task> act = () =>
            sut.RunAsync(
                BuildPlan(incoming, outgoing, primary),
                AnimationSpeed.Off,
                CancellationToken.None
            );

        await act.Should().NotThrowAsync().ConfigureAwait(true);
        // Incoming window still resized; primary still brought to front.
        windows.Received().Resize(new IntPtr(0xA), Arg.Any<Rect>());
        windows.Received().BringToFront(new IntPtr(0xA));
    }
}
