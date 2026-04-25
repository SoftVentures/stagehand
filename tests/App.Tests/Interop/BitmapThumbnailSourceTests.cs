using System.Diagnostics.CodeAnalysis;
using App.Interop;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests.Interop;

/// <summary>
/// Behavioural coverage for <see cref="BitmapThumbnailSource"/>. The class is
/// a passive data container; tests verify property round-trips, dispose
/// idempotency, and post-dispose throw behaviour.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class BitmapThumbnailSourceTests
{
    [Fact]
    public void UpdateDestinationRect_Stores_Rect_And_Opacity()
    {
        BitmapThumbnailSource source = MakeSource();

        source.UpdateDestinationRect(new Rect(10, 20, 110, 220), opacity: 200);

        source.DestinationRect.Should().Be(new Rect(10, 20, 110, 220));
        source.Opacity.Should().Be((byte)200);
    }

    [Fact]
    public void UpdateDestinationRect_Defaults_To_Fully_Opaque()
    {
        BitmapThumbnailSource source = MakeSource();

        source.UpdateDestinationRect(new Rect(0, 0, 100, 100));

        source.Opacity.Should().Be((byte)255);
    }

    [Fact]
    public void Snapshot_Property_Returns_The_Original_Snapshot()
    {
        var snapshot = new ThumbnailSnapshot(new Size(8, 8), new byte[256], 32);
        var source = new BitmapThumbnailSource(
            new IntPtr(0x1),
            snapshot,
            NullLoggerFactory.Instance.CreateLogger<BitmapThumbnailSource>()
        );

        source.Snapshot.Should().BeSameAs(snapshot);
        source.SourceSize.Should().Be(new Size(8, 8));
    }

    [Fact]
    public void Dispose_Drops_Snapshot_Reference()
    {
        BitmapThumbnailSource source = MakeSource();

        source.Dispose();

        source.Snapshot.Should().BeNull("Dispose releases the pixel buffer for the GC");
        source.SourceSize.Should().Be(Size.Empty);
    }

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        BitmapThumbnailSource source = MakeSource();
        source.Dispose();

        Action second = () => source.Dispose();

        second.Should().NotThrow("double-dispose must be a no-op");
    }

    [Fact]
    public void UpdateDestinationRect_Throws_After_Dispose()
    {
        BitmapThumbnailSource source = MakeSource();
        source.Dispose();

        Action act = () => source.UpdateDestinationRect(new Rect(0, 0, 1, 1));

        act.Should().Throw<ObjectDisposedException>();
    }

    private static BitmapThumbnailSource MakeSource() =>
        new(
            new IntPtr(0x42),
            new ThumbnailSnapshot(new Size(16, 16), new byte[16 * 16 * 4], 16 * 4),
            NullLoggerFactory.Instance.CreateLogger<BitmapThumbnailSource>()
        );
}
