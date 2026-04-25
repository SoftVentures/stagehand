using System.Diagnostics.CodeAnalysis;
using App.Interop;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behavioural coverage for <see cref="ThumbnailSourceFactory"/>. The class
/// is pure delegation, so tests verify routing only — no logic to exercise.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class ThumbnailSourceFactoryTests
{
    [Fact]
    public void RegisterDwm_Routes_To_Dwm_Factory()
    {
        IDwmThumbnailFactory dwm = Substitute.For<IDwmThumbnailFactory>();
        IBitmapThumbnailFactory bitmap = Substitute.For<IBitmapThumbnailFactory>();
        var source = new IntPtr(0x1);
        var destination = new IntPtr(0x2);

        var factory = new ThumbnailSourceFactory(dwm, bitmap);

        // The mocked dwm factory returns null for Register (DwmThumbnail is a
        // class with no parameterless constructor; instantiating one would
        // require booting DWM). Routing is what we verify — the Composite
        // pipes the arguments straight through and returns whatever the
        // underlying factory hands back.
        _ = factory.RegisterDwm(source, destination);

        dwm.Received(1).Register(source, destination);
        bitmap.DidNotReceiveWithAnyArgs().Register(default, null!);
    }

    [Fact]
    public void RegisterBitmap_Routes_To_Bitmap_Factory()
    {
        IDwmThumbnailFactory dwm = Substitute.For<IDwmThumbnailFactory>();
        IBitmapThumbnailFactory bitmap = Substitute.For<IBitmapThumbnailFactory>();
        var source = new IntPtr(0x10);
        var snapshot = new ThumbnailSnapshot(new Size(4, 4), new byte[64], 16);

        var factory = new ThumbnailSourceFactory(dwm, bitmap);
        _ = factory.RegisterBitmap(source, snapshot);

        bitmap.Received(1).Register(source, snapshot);
        dwm.DidNotReceiveWithAnyArgs().Register(default, default);
    }

    [Fact]
    public void Capture_Routes_To_Bitmap_Factory()
    {
        IDwmThumbnailFactory dwm = Substitute.For<IDwmThumbnailFactory>();
        IBitmapThumbnailFactory bitmap = Substitute.For<IBitmapThumbnailFactory>();
        var hwnd = new IntPtr(0x20);
        var expectedSnapshot = new ThumbnailSnapshot(new Size(2, 2), new byte[16], 8);
        bitmap.Capture(hwnd).Returns(expectedSnapshot);

        var factory = new ThumbnailSourceFactory(dwm, bitmap);
        ThumbnailSnapshot? actual = factory.Capture(hwnd);

        actual.Should().BeSameAs(expectedSnapshot);
        bitmap.Received(1).Capture(hwnd);
    }

    [Fact]
    public void Constructor_Throws_On_Null_Dwm()
    {
        IBitmapThumbnailFactory bitmap = Substitute.For<IBitmapThumbnailFactory>();
        Action act = () => _ = new ThumbnailSourceFactory(null!, bitmap);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_Throws_On_Null_Bitmap()
    {
        IDwmThumbnailFactory dwm = Substitute.For<IDwmThumbnailFactory>();
        Action act = () => _ = new ThumbnailSourceFactory(dwm, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
