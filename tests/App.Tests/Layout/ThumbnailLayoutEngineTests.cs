using System.Diagnostics.CodeAnalysis;
using App.Core.Layout;
using App.Core.Stage;
using App.Interop;
using FluentAssertions;
using Xunit;
using Size = App.Core.Layout.Size;

namespace App.Tests.Layout;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class ThumbnailLayoutEngineTests
{
    // Tolerance used when asserting equality on rounded integer rectangles.
    private const int RoundingTolerance = 1;

    private static WindowIdentity Id(int i) => new((IntPtr)(100 + i), 1000 + i, 1234L + i);

    private static LayoutRequest Request(
        Rect sidebar,
        IReadOnlyList<(WindowIdentity, Size)> windows,
        double spacing = 8.0,
        double padding = 12.0,
        double maxH = 300.0
    ) => new(sidebar, spacing, padding, maxH, windows);

    [Fact]
    public void Empty_WindowList_ReturnsEmptyPlacements()
    {
        var engine = new ThumbnailLayoutEngine();
        LayoutRequest req = Request(new Rect(0, 0, 200, 1000), []);

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SingleWindow_MatchingSidebarAspectRatio_FillsUsableWidthAndHeight()
    {
        var engine = new ThumbnailLayoutEngine();
        // Sidebar usable area: 200-2*10 = 180 wide, 400-2*10 = 380 tall (no spacing, N=1).
        // Source aspect = 180/380 = same as usable area → thumbnail occupies full usable area.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(180, 380)) };
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 400),
            windows,
            spacing: 8.0,
            padding: 10.0,
            maxH: 1000.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        result.Should().HaveCount(1);
        Rect p = result[0].DestinationRect;
        p.Width.Should().BeCloseTo(180, RoundingTolerance);
        p.Height.Should().BeCloseTo(380, RoundingTolerance);
        p.X.Should().BeCloseTo(10, RoundingTolerance);
        p.Y.Should().BeCloseTo(10, RoundingTolerance);
    }

    [Fact]
    public void SingleWindow_WiderThanSidebar_IsWidthCappedAndHeightReDerived()
    {
        var engine = new ThumbnailLayoutEngine();
        // Sidebar 200 wide, padding 10 → usable width 180.
        // Source 1920x1080 (landscape) → at provisional h=300 width would be 533 → capped at 180, height becomes 180*(1080/1920)=101.25.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(1920, 1080)) };
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 8.0,
            padding: 10.0,
            maxH: 300.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        Rect p = result[0].DestinationRect;
        p.Width.Should().BeCloseTo(180, RoundingTolerance);
        p.Height.Should().BeCloseTo(101, RoundingTolerance);
    }

    [Fact]
    public void SingleWindow_TallerThanSidebar_IsHeightCappedByMaxThumbnailHeight()
    {
        var engine = new ThumbnailLayoutEngine();
        // Source 400x1200 (portrait). Usable width 180. Usable height 1000-20 = 980.
        // Provisional h = min(300, 980/1) = 300. w = 300*(400/1200) = 100 ≤ 180 → no width cap.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(400, 1200)) };
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 8.0,
            padding: 10.0,
            maxH: 300.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        Rect p = result[0].DestinationRect;
        p.Height.Should().BeCloseTo(300, RoundingTolerance); // capped by MaxThumbnailHeight
        p.Width.Should().BeCloseTo(100, RoundingTolerance); // 300 * 400/1200
    }

    [Fact]
    public void FiveEqualWindows_AreStackedVerticallyWithSpacing()
    {
        var engine = new ThumbnailLayoutEngine();
        var windows = Enumerable.Range(0, 5).Select(i => (Id(i), new Size(160, 90))).ToList();
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 8.0,
            padding: 10.0,
            maxH: 300.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        result.Should().HaveCount(5);

        // All thumbnails share the same width and height (equal sources).
        var widths = result.Select(r => r.DestinationRect.Width).Distinct().ToList();
        var heights = result.Select(r => r.DestinationRect.Height).Distinct().ToList();
        widths.Should().HaveCount(1);
        heights.Should().HaveCount(1);

        // Thumbnails are stacked top-to-bottom in input order with the configured spacing.
        for (var i = 1; i < result.Count; i++)
        {
            var prevBottom = result[i - 1].DestinationRect.Bottom;
            var thisTop = result[i].DestinationRect.Y;
            (thisTop - prevBottom).Should().BeCloseTo(8, RoundingTolerance);
        }

        // Input identities survive in order.
        for (var i = 0; i < result.Count; i++)
        {
            result[i].Identity.Should().Be(windows[i].Item1);
        }
    }

    [Fact]
    public void TwentyWindows_AreHeightLimitedToFitInsideSidebar()
    {
        var engine = new ThumbnailLayoutEngine();
        var windows = Enumerable.Range(0, 20).Select(i => (Id(i), new Size(160, 90))).ToList();
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 4.0,
            padding: 10.0,
            maxH: 300.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        result.Should().HaveCount(20);

        // Total vertical footprint stays inside the sidebar's usable area
        // (bottom of last thumbnail <= sidebar bottom minus padding).
        var lastBottom = result[^1].DestinationRect.Bottom;
        var allowedBottom = req.SidebarBounds.Bottom - (int)req.OuterPadding;
        lastBottom.Should().BeLessThanOrEqualTo(allowedBottom + RoundingTolerance);

        // Provisional h = (1000 - 20 - 19*4) / 20 = (1000 - 20 - 76) / 20 = 45.2
        // which is also the actual h because landscape 16:9 source fits easily in the
        // 180 px usable width (w = 45.2 * 160/90 ≈ 80.4).
        result[0].DestinationRect.Height.Should().BeCloseTo(45, RoundingTolerance);
    }

    [Fact]
    public void SpacingAndPadding_AreHonoured()
    {
        var engine = new ThumbnailLayoutEngine();
        var windows = new List<(WindowIdentity, Size)>
        {
            (Id(1), new Size(100, 100)),
            (Id(2), new Size(100, 100)),
            (Id(3), new Size(100, 100)),
        };
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 16.0,
            padding: 20.0,
            maxH: 300.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        // Outer padding: the first thumbnail starts at sidebar.Y + padding only when
        // the stack is not vertically centred. With maxH=300 (capping h at 300), the
        // stack is shorter than the usable area, so it is centred. But the *horizontal*
        // distance from the sidebar edges still respects padding: usable width = 200-40=160,
        // square thumbnails at h=300 would be 300 wide → width-capped to 160, height=160.
        // So each thumbnail is 160x160, horizontally flush with padding=20 on each side.
        foreach (ThumbnailPlacement placement in result)
        {
            placement.DestinationRect.X.Should().BeCloseTo(20, RoundingTolerance);
            placement.DestinationRect.Width.Should().BeCloseTo(160, RoundingTolerance);
        }

        // Gaps between consecutive thumbnails match the spacing value.
        for (var i = 1; i < result.Count; i++)
        {
            var prevBottom = result[i - 1].DestinationRect.Bottom;
            var thisTop = result[i].DestinationRect.Y;
            (thisTop - prevBottom).Should().BeCloseTo(16, RoundingTolerance);
        }
    }

    [Fact]
    public void LeftAndRightEdgeSidebars_ProduceIdenticalRelativeLayouts()
    {
        var engine = new ThumbnailLayoutEngine();
        var windows = new List<(WindowIdentity, Size)>
        {
            (Id(1), new Size(200, 100)),
            (Id(2), new Size(100, 200)),
            (Id(3), new Size(160, 90)),
        };

        // Left sidebar: origin (0,0), width 240, height 900.
        LayoutRequest leftReq = Request(new Rect(0, 0, 240, 900), windows);

        // Right sidebar: same monitor 1920 wide, docked on the right with same width.
        var rightSidebar = new Rect(1920 - 240, 0, 240, 900);
        LayoutRequest rightReq = Request(rightSidebar, windows);

        IReadOnlyList<ThumbnailPlacement> leftResult = engine.Compute(leftReq);
        IReadOnlyList<ThumbnailPlacement> rightResult = engine.Compute(rightReq);

        leftResult.Should().HaveCount(rightResult.Count);

        for (var i = 0; i < leftResult.Count; i++)
        {
            Rect l = leftResult[i].DestinationRect;
            Rect r = rightResult[i].DestinationRect;

            // Sizes must be identical: the geometry of a sidebar does not depend on
            // which edge of the monitor it is docked to.
            l.Width.Should().Be(r.Width);
            l.Height.Should().Be(r.Height);
            l.Y.Should().Be(r.Y); // same vertical positioning

            // Horizontal offsets relative to each sidebar's own X origin match.
            var lRelX = l.X - leftReq.SidebarBounds.X;
            var rRelX = r.X - rightReq.SidebarBounds.X;
            lRelX.Should().Be(rRelX);
        }
    }

    [Fact]
    public void MaxThumbnailHeight_CapsProvisionalHeight()
    {
        var engine = new ThumbnailLayoutEngine();
        // Tall sidebar, single window, small max height → thumbnail height equals maxH,
        // width is re-derived from the source aspect ratio.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(200, 200)) };
        LayoutRequest req = Request(
            new Rect(0, 0, 400, 5000),
            windows,
            spacing: 0.0,
            padding: 0.0,
            maxH: 120.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        Rect p = result[0].DestinationRect;
        p.Height.Should().BeCloseTo(120, RoundingTolerance);
        p.Width.Should().BeCloseTo(120, RoundingTolerance); // square source at 120 high
    }

    [Fact]
    public void StackShorterThanSidebar_IsCentredVertically()
    {
        var engine = new ThumbnailLayoutEngine();
        // One small square thumbnail in a tall sidebar. With maxH=100 the thumbnail is
        // 100x100. Usable height = 1000 - 2*10 = 980. Slack = 980 - 100 = 880 → the
        // thumbnail should sit at Y = padding + slack/2 = 10 + 440 = 450.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(100, 100)) };
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 8.0,
            padding: 10.0,
            maxH: 100.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        Rect p = result[0].DestinationRect;
        p.Y.Should().BeCloseTo(450, RoundingTolerance);
        p.Height.Should().BeCloseTo(100, RoundingTolerance);
    }

    [Fact]
    public void ThumbnailsAreHorizontallyCentredInsideSidebar()
    {
        var engine = new ThumbnailLayoutEngine();
        // Portrait source → thumbnail narrower than sidebar → must be centred horizontally.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(50, 500)) };
        LayoutRequest req = Request(
            new Rect(100, 0, 200, 1000),
            windows,
            spacing: 0.0,
            padding: 10.0,
            maxH: 300.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        Rect p = result[0].DestinationRect;
        // Thumbnail width = 300*(50/500) = 30.
        p.Width.Should().BeCloseTo(30, RoundingTolerance);
        // Centred inside sidebar [100, 300]: centre at 200, X = 200 - 15 = 185.
        p.X.Should().BeCloseTo(185, RoundingTolerance);
    }

    [Fact]
    public void ZeroSizedSourceWindow_IsHandledAsSquarePlaceholder()
    {
        var engine = new ThumbnailLayoutEngine();
        // Risk mitigation per Plan 02 §Risks: DWM may report size (0,0) — the engine
        // must not divide by zero; a 1x1 placeholder (square) is substituted.
        var windows = new List<(WindowIdentity, Size)> { (Id(1), new Size(0, 0)) };
        LayoutRequest req = Request(
            new Rect(0, 0, 200, 1000),
            windows,
            spacing: 0.0,
            padding: 10.0,
            maxH: 120.0
        );

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        Rect p = result[0].DestinationRect;
        p.Height.Should().BeCloseTo(120, RoundingTolerance);
        p.Width.Should().BeCloseTo(120, RoundingTolerance); // 1:1 aspect preserved
    }

    [Fact]
    public void IdentitiesAreReturnedInInputOrder()
    {
        var engine = new ThumbnailLayoutEngine();
        var windows = new List<(WindowIdentity, Size)>
        {
            (Id(42), new Size(100, 100)),
            (Id(7), new Size(100, 100)),
            (Id(13), new Size(100, 100)),
        };
        LayoutRequest req = Request(new Rect(0, 0, 200, 1000), windows);

        IReadOnlyList<ThumbnailPlacement> result = engine.Compute(req);

        result.Select(r => r.Identity).Should().Equal(windows.Select(w => w.Item1));
    }

    [Fact]
    public void Compute_NullRequest_Throws()
    {
        var engine = new ThumbnailLayoutEngine();

        Func<IReadOnlyList<ThumbnailPlacement>> act = () => engine.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
