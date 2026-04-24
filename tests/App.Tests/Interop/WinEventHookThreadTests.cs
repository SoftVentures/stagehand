using System.Diagnostics.CodeAnalysis;
using App.Interop.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behaviour tests for <see cref="WinEventHookThread"/>. Uses the real Win32
/// message pump — the class is pure interop infrastructure without a seam
/// for the pump itself. Each test creates its own thread instance so STA /
/// GetMessage interactions cannot bleed between cases.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
[SuppressMessage(
    "Usage",
    "xUnit1031:Do not use blocking task operations in test method",
    Justification = "WinEventHookThread tests deliberately synchronise on cross-thread Task results with bounded timeouts to detect wedged state."
)]
public sealed class WinEventHookThreadTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Thread_Starts_On_First_InvokeAsync_And_Runs_Work()
    {
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);

        var observed = 0;
        Task task = thread.InvokeAsync(() => observed = 42);

        await task.WaitAsync(TestTimeout).ConfigureAwait(true);
        observed.Should().Be(42);
    }

    [Fact]
    public async Task InvokeAsync_Round_Trips_Multiple_Items_In_Order()
    {
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);

        var results = new List<int>();
        var lockObj = new object();
        void Add(int v)
        {
            lock (lockObj)
            {
                results.Add(v);
            }
        }

        Task t1 = thread.InvokeAsync(() => Add(1));
        Task t2 = thread.InvokeAsync(() => Add(2));
        Task t3 = thread.InvokeAsync(() => Add(3));

        await Task.WhenAll(t1, t2, t3).WaitAsync(TestTimeout).ConfigureAwait(true);

        results.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task InvokeAsync_Surfaces_Action_Exceptions_Via_Task()
    {
        using var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);

        Func<Task> act = () =>
            thread.InvokeAsync(() => throw new InvalidOperationException("boom"));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("boom")
            .ConfigureAwait(true);
    }

    [Fact]
    public void Dispose_Before_Any_InvokeAsync_Is_A_NoOp()
    {
        var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        Action action = () => thread.Dispose();
        action.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_Shuts_Down_Cleanly()
    {
        var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        await thread.InvokeAsync(() => { }).WaitAsync(TestTimeout).ConfigureAwait(true);

        Action action = () => thread.Dispose();
        action.Should().NotThrow();
    }

    [Fact]
    public async Task Post_Shutdown_InvokeAsync_Throws_ObjectDisposedException()
    {
        var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        await thread.InvokeAsync(() => { }).WaitAsync(TestTimeout).ConfigureAwait(true);
        thread.Dispose();

        Action post = () =>
        {
            _ = thread.InvokeAsync(() => { });
        };
        post.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Double_Dispose_Is_Idempotent()
    {
        var thread = new WinEventHookThread(NullLogger<WinEventHookThread>.Instance);
        await thread.InvokeAsync(() => { }).WaitAsync(TestTimeout).ConfigureAwait(true);
        thread.Dispose();

        Action second = () => thread.Dispose();
        second.Should().NotThrow();
    }
}
