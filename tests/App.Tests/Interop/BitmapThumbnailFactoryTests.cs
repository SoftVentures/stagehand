using System.Diagnostics.CodeAnalysis;
using App.Interop;
using App.Interop.Internal;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behavioural coverage for <see cref="BitmapThumbnailFactory"/>. Both seams
/// are substituted so the capture chain is exercised end-to-end without GDI.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class BitmapThumbnailFactoryTests
{
    [Fact]
    public void Capture_Returns_Null_For_Zero_Width_Window()
    {
        (INativeWindowApi window, INativeBitmapApi bitmap, BitmapThumbnailFactory factory) =
            BuildFactory();

        var hwnd = new IntPtr(0x100);
        window.GetWindowRect(hwnd).Returns(new Rect(0, 0, 0, 200));

        ThumbnailSnapshot? result = factory.Capture(hwnd);

        result.Should().BeNull("a zero-width window cannot be captured");
        bitmap.DidNotReceiveWithAnyArgs().CaptureWindow(default, default, default, default);
    }

    [Fact]
    public void Capture_Returns_Null_For_Zero_Height_Window()
    {
        (INativeWindowApi window, INativeBitmapApi bitmap, BitmapThumbnailFactory factory) =
            BuildFactory();

        var hwnd = new IntPtr(0x101);
        window.GetWindowRect(hwnd).Returns(new Rect(0, 0, 200, 0));

        ThumbnailSnapshot? result = factory.Capture(hwnd);

        result.Should().BeNull();
        bitmap.DidNotReceiveWithAnyArgs().CaptureWindow(default, default, default, default);
    }

    [Fact]
    public void Capture_Returns_Null_For_Negative_Bounds()
    {
        // Defensive: a clipped or partially-park-staged window could in
        // principle report a negative-extent rect. Capture must reject it
        // without invoking the native pipeline.
        (INativeWindowApi window, INativeBitmapApi bitmap, BitmapThumbnailFactory factory) =
            BuildFactory();

        var hwnd = new IntPtr(0x102);
        window.GetWindowRect(hwnd).Returns(new Rect(0, 0, -50, -50));

        ThumbnailSnapshot? result = factory.Capture(hwnd);

        result.Should().BeNull();
        bitmap.DidNotReceiveWithAnyArgs().CaptureWindow(default, default, default, default);
    }

    [Fact]
    public void Capture_Forwards_PW_RENDERFULLCONTENT_Flag_To_Native()
    {
        (INativeWindowApi window, INativeBitmapApi bitmap, BitmapThumbnailFactory factory) =
            BuildFactory();

        var hwnd = new IntPtr(0x200);
        window.GetWindowRect(hwnd).Returns(new Rect(0, 0, 800, 600));
        bitmap
            .CaptureWindow(hwnd, 800, 600, NativeMethods.PW_RENDERFULLCONTENT)
            .Returns(
                new ThumbnailSnapshot(new Size(800, 600), new byte[800 * 600 * 4], Stride: 800 * 4)
            );

        ThumbnailSnapshot? result = factory.Capture(hwnd);

        result.Should().NotBeNull();
        result!.SourceSize.Should().Be(new Size(800, 600));
        bitmap.Received(1).CaptureWindow(hwnd, 800, 600, NativeMethods.PW_RENDERFULLCONTENT);
    }

    [Fact]
    public void Capture_Returns_Null_When_Native_Capture_Fails()
    {
        (INativeWindowApi window, INativeBitmapApi bitmap, BitmapThumbnailFactory factory) =
            BuildFactory();

        var hwnd = new IntPtr(0x300);
        window.GetWindowRect(hwnd).Returns(new Rect(0, 0, 100, 100));
        bitmap
            .CaptureWindow(hwnd, 100, 100, NativeMethods.PW_RENDERFULLCONTENT)
            .Returns((ThumbnailSnapshot?)null);

        ThumbnailSnapshot? result = factory.Capture(hwnd);

        result.Should().BeNull();
    }

    [Fact]
    public void Register_Wraps_Snapshot_With_Source_Hwnd_And_Size()
    {
        (INativeWindowApi _, INativeBitmapApi _, BitmapThumbnailFactory factory) = BuildFactory();

        var hwnd = new IntPtr(0x400);
        var snapshot = new ThumbnailSnapshot(new Size(64, 32), new byte[64 * 32 * 4], 64 * 4);

        BitmapThumbnailSource source = factory.Register(hwnd, snapshot);

        source.Source.Should().Be(hwnd);
        source.SourceSize.Should().Be(new Size(64, 32));
        source.Snapshot.Should().BeSameAs(snapshot);
    }

    [Fact]
    public void Register_Throws_On_Null_Snapshot()
    {
        (INativeWindowApi _, INativeBitmapApi _, BitmapThumbnailFactory factory) = BuildFactory();
        Action act = () => factory.Register(new IntPtr(0x1), snapshot: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static (
        INativeWindowApi WindowApi,
        INativeBitmapApi BitmapApi,
        BitmapThumbnailFactory Factory
    ) BuildFactory()
    {
        INativeWindowApi window = Substitute.For<INativeWindowApi>();
        INativeBitmapApi bitmap = Substitute.For<INativeBitmapApi>();
        var factory = new BitmapThumbnailFactory(window, bitmap, NullLoggerFactory.Instance);
        return (window, bitmap, factory);
    }
}
