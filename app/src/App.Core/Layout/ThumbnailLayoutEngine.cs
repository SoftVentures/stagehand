using App.Interop;

namespace App.Core.Layout;

/// <summary>
/// Pure-function implementation of <see cref="IThumbnailLayoutEngine"/>.
/// </summary>
/// <remarks>
/// The algorithm follows Plan 02 §Design.7 verbatim:
/// <list type="number">
/// <item>Usable height <c>H = SidebarBounds.Height - 2*OuterPadding - (N-1)*ThumbnailSpacing</c>.</item>
/// <item>Provisional per-thumbnail height <c>h = min(MaxThumbnailHeight, H / N)</c>.</item>
/// <item>Per-source width <c>w = h * (sourceWidth / sourceHeight)</c>; cap at usable width and re-derive height if capped.</item>
/// <item>Centre horizontally within the sidebar.</item>
/// <item>Stack vertically with <c>ThumbnailSpacing</c>; centre the stack if total stack height &lt; usable height.</item>
/// <item>Empty <c>Windows</c> → empty list.</item>
/// </list>
/// No state, no dependencies.
/// </remarks>
public sealed class ThumbnailLayoutEngine : IThumbnailLayoutEngine
{
    /// <inheritdoc />
    public IReadOnlyList<ThumbnailPlacement> Compute(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Step 6 — short-circuit on empty input.
        if (request.Windows.Count == 0)
        {
            return [];
        }

        var n = request.Windows.Count;
        var outerPadding = Math.Max(0.0, request.OuterPadding);
        var spacing = Math.Max(0.0, request.ThumbnailSpacing);
        var maxH = Math.Max(0.0, request.MaxThumbnailHeight);

        var sidebarWidth = Math.Max(0.0, request.SidebarBounds.Width);
        var sidebarHeight = Math.Max(0.0, request.SidebarBounds.Height);

        var usableWidth = Math.Max(0.0, sidebarWidth - (2.0 * outerPadding));

        // Step 1 — usable vertical height for the N thumbnails plus the (N-1) inter-thumb gaps.
        var usableHeight = sidebarHeight - (2.0 * outerPadding) - ((n - 1) * spacing);
        usableHeight = Math.Max(0.0, usableHeight);

        // Step 2 — provisional per-thumbnail height.
        var provisionalH = Math.Min(maxH, usableHeight / n);
        provisionalH = Math.Max(0.0, provisionalH);

        var placements = new ThumbnailPlacement[n];

        // Precompute per-window sizes (step 3) so we can measure the total stack
        // height before laying out (step 5 needs it for the vertical centering).
        var sizes = new (double Width, double Height)[n];
        var stackHeight = 0.0;
        for (var i = 0; i < n; i++)
        {
            (_, Size src) = request.Windows[i];

            // Risk mitigation (see Plan 02 §Risks & Mitigations): treat zero/negative
            // source dimensions as a 1x1 placeholder so the aspect ratio stays finite.
            var srcW = src.Width > 0 ? src.Width : 1.0;
            var srcH = src.Height > 0 ? src.Height : 1.0;

            var h = provisionalH;
            var w = h * (srcW / srcH);

            // Step 3 continued — width cap re-derives height to preserve aspect ratio.
            if (w > usableWidth)
            {
                w = usableWidth;
                h = (srcH / srcW) * w;
            }

            w = Math.Max(0.0, w);
            h = Math.Max(0.0, h);

            sizes[i] = (w, h);
            stackHeight += h;
        }
        stackHeight += (n - 1) * spacing;

        // Step 5 — vertical origin: top padding plus any extra slack centred.
        var slack = Math.Max(0.0, usableHeight - stackHeight);
        var y = request.SidebarBounds.Y + outerPadding + (slack / 2.0);

        var sidebarCentreX = request.SidebarBounds.X + (sidebarWidth / 2.0);

        for (var i = 0; i < n; i++)
        {
            (var w, var h) = sizes[i];

            // Step 4 — centre horizontally within the sidebar.
            var x = sidebarCentreX - (w / 2.0);

            Rect dest = new(
                (int)Math.Round(x),
                (int)Math.Round(y),
                (int)Math.Round(w),
                (int)Math.Round(h)
            );

            placements[i] = new ThumbnailPlacement(request.Windows[i].Identity, dest);

            y += h + spacing;
        }

        return placements;
    }
}
