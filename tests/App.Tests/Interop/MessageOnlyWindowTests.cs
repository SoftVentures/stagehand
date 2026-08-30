using System.Diagnostics.CodeAnalysis;
using App.Interop;
using App.Interop.Threading;
using FluentAssertions;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behaviour tests for <see cref="MessageOnlyWindow"/>. Uses real Win32 (the
/// class IS the seam over CreateWindowEx / DestroyWindow). Tests synthesise
/// messages with <c>SendMessage</c>, which calls WndProc synchronously and
/// avoids needing a message pump.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class MessageOnlyWindowTests
{
    [Fact]
    public void Constructor_Creates_Hwnd()
    {
        using var window = new MessageOnlyWindow();
        window.Hwnd.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void Two_Instances_Coexist_With_Different_Class_Names()
    {
        using var a = new MessageOnlyWindow();
        using var b = new MessageOnlyWindow();
        a.Hwnd.Should().NotBe(b.Hwnd);
    }

    [Fact]
    public void MessageReceived_Fires_For_Sent_Message()
    {
        using var window = new MessageOnlyWindow();

        const uint testMsg = NativeMethods.WM_USER + 7;
        var captured = new List<uint>();
        window.MessageReceived += (_, e) => captured.Add(e.Message);

        _ = NativeMethods.SendMessage(window.Hwnd, testMsg, IntPtr.Zero, IntPtr.Zero);

        captured.Should().Contain(testMsg);
    }

    [Fact]
    public void MessageReceived_Carries_WParam_And_LParam()
    {
        using var window = new MessageOnlyWindow();

        const uint testMsg = NativeMethods.WM_USER + 8;
        IntPtr capturedW = IntPtr.Zero;
        IntPtr capturedL = IntPtr.Zero;
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == testMsg)
            {
                capturedW = e.WParam;
                capturedL = e.LParam;
            }
        };

        _ = NativeMethods.SendMessage(window.Hwnd, testMsg, new IntPtr(0x1234), new IntPtr(0x5678));

        capturedW.Should().Be(new IntPtr(0x1234));
        capturedL.Should().Be(new IntPtr(0x5678));
    }

    [Fact]
    public void Subscriber_Exception_Does_Not_Bubble_Out()
    {
        using var window = new MessageOnlyWindow();

        const uint testMsg = NativeMethods.WM_USER + 9;
        window.MessageReceived += (_, _) => throw new InvalidOperationException("boom");

        Action act = () =>
            NativeMethods.SendMessage(window.Hwnd, testMsg, IntPtr.Zero, IntPtr.Zero);
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var window = new MessageOnlyWindow();
        window.Dispose();
        Action second = window.Dispose;
        second.Should().NotThrow();
    }

    [Fact]
    public void Dispose_Invalidates_Hwnd()
    {
        var window = new MessageOnlyWindow();
        window.Dispose();
        window.Hwnd.Should().Be(IntPtr.Zero);
    }
}
