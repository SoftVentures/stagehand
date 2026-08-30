using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using App.Core.Branding;
using App.Core.Layout;
using App.Core.Stage;
using App.Interop;
using Microsoft.Extensions.Logging;
using CoreSize = App.Core.Layout.Size;
using Rect = App.Interop.Rect;

namespace App.Shell.Overlay;

/// <summary>
/// Which edge of the managed monitor the sidebar docks to. Plan 04 makes
/// this user-configurable; Plan 02 hard-codes <see cref="Left"/>.
/// </summary>
public enum SidebarEdge
{
    /// <summary>Dock to the left edge of the monitor's work area.</summary>
    Left,

    /// <summary>Dock to the right edge of the monitor's work area.</summary>
    Right,
}

/// <summary>
/// One WPF window per managed monitor, holding DWM-thumbnail miniatures of
/// parked windows. See Plan 02 §Design.8.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coordinate systems.</b> WPF window placement (<see cref="Window.Left"/>
/// et al.) is in device-independent units (DIPs). The Win32 monitor APIs
/// return physical pixels. The DWM thumbnail destination rect must be in
/// physical pixels relative to the destination HWND's client area (and the
/// destination HWND is this overlay). We therefore feed the layout engine a
/// sidebar rect of <c>(0, 0, widthPx, heightPx)</c> where <c>widthPx</c> and
/// <c>heightPx</c> are the overlay's physical-pixel dimensions (WPF size
/// times <c>DpiScale</c>).
/// </para>
/// <para>
/// <b>Lifecycle.</b> The overlay does NOT claim DI ownership — it is created
/// manually by whichever consumer wants a sidebar (today: the harness in
/// Plan 02 §S9; tomorrow: the stage controller introduced in Plan 03).
/// <see cref="HideAndRelease"/> disposes the thumbnails <em>before</em>
/// closing the window so DWM sees the unregister calls while the destination
/// HWND is still valid.
/// </para>
/// </remarks>
public sealed partial class SidebarOverlay : Window, ISidebarOverlayHandle
{
    private const int WmDpiChanged = 0x02E0;

    // Layout knobs — Plan 02 keeps them fixed. Plan 04 promotes them to settings.
    private const double DefaultThumbnailSpacing = 8.0;
    private const double DefaultOuterPadding = 8.0;
    private const double DefaultMaxThumbnailHeight = 160.0;

    private static readonly Action<ILogger, int, Exception?> s_logSync = LoggerMessage.Define<int>(
        LogLevel.Trace,
        new EventId(5001, nameof(SidebarOverlay) + ".Sync"),
        "SidebarOverlay.Sync: {Count} window(s)."
    );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logRegisterFailed =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Debug,
            new EventId(5002, nameof(SidebarOverlay) + ".RegisterFailed"),
            "SidebarOverlay: failed to register thumbnail for HWND 0x{SourceHwnd:X}."
        );

    private static readonly Action<ILogger, Exception?> s_logDpiChanged = LoggerMessage.Define(
        LogLevel.Trace,
        new EventId(5003, nameof(SidebarOverlay) + ".DpiChanged"),
        "SidebarOverlay: WM_DPICHANGED — recomputing layout."
    );

    private static readonly Action<ILogger, Exception?> s_logShowFault = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(5004, nameof(SidebarOverlay) + ".ShowFault"),
        "SidebarOverlay: Show() threw."
    );

    private static readonly Action<ILogger, Exception?> s_logSyncFault = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(5005, nameof(SidebarOverlay) + ".SyncFault"),
        "SidebarOverlay: Sync() threw."
    );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logMonitorFallback =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(5006, nameof(SidebarOverlay) + ".MonitorFallback"),
            "SidebarOverlay.ShowOn: monitor handle 0x{Monitor:X} was NULL/unknown; falling back to primary monitor."
        );

    private readonly IDwmThumbnailFactory _thumbnails;
    private readonly IThumbnailLayoutEngine _layout;
    private readonly ILogger<SidebarOverlay> _log;

    private readonly Dictionary<WindowIdentity, DwmThumbnail> _liveThumbnails = [];
    private IReadOnlyList<WindowSnapshot> _lastWindows = [];
    private OverlayMonitor? _monitor;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _styleApplied;

    // Plan 03 §S4: track which scene each tile represents, so WM_LBUTTONUP
    // can hit-test against tile rectangles and raise TileClicked. Captured
    // by SyncScenes; cleared on the next sync.
    private readonly List<SceneTileEntry> _sceneTiles = [];

    // Last destination rect applied to each thumbnail, used for hit-testing.
    // Maintained by RecomputeAndApplyLayout.
    private readonly Dictionary<WindowIdentity, Rect> _lastTileRects = [];

    private sealed record SceneTileEntry(SceneId Scene, WindowIdentity Primary, int WindowCount);

    /// <summary>
    /// Raised when the user left-clicks a scene tile inside the overlay.
    /// The event fires on the overlay's UI thread.
    /// </summary>
    public event EventHandler<SidebarTileClickedEventArgs>? TileClicked;

    /// <summary>
    /// DI-friendly constructor. The overlay is NOT a DI singleton (see
    /// class remarks); callers typically <c>new</c> one per monitor on
    /// demand, passing the resolved services explicitly.
    /// </summary>
    public SidebarOverlay(
        IDwmThumbnailFactory thumbnails,
        IThumbnailLayoutEngine layout,
        ILogger<SidebarOverlay> log
    )
    {
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);
        _thumbnails = thumbnails;
        _layout = layout;
        _log = log;

        InitializeComponent();
        Title = BrandConstants.OverlayWindowTitle;

        // Plan 03 §S4 click path. Using WPF's tunneling Preview event
        // (rather than the WM_LBUTTONUP path through HwndSource.AddHook)
        // is more reliable on AllowsTransparency=True windows: the WndProc
        // hook fires inconsistently when the layered-window compositor
        // decides a near-zero-alpha pixel is "click-through". The window
        // Background must have meaningful alpha (>=~25%) for Win32 to
        // route the mouse event into the WPF surface at all — see the
        // SidebarOverlay.xaml Background note.
        PreviewMouseLeftButtonDown += OnSidebarLeftButtonDown;
        MouseMove += OnSidebarMouseMove;
        MouseLeave += OnSidebarMouseLeave;
    }

    /// <summary>
    /// Which edge of the monitor the sidebar docks to. Plan 04 promotes this
    /// to a setting; Plan 02 consumers may override before <see cref="ShowOn"/>.
    /// </summary>
    public SidebarEdge Edge { get; set; } = SidebarEdge.Left;

    /// <summary>
    /// Sidebar width in device-independent units (DIPs). Plan 04 promotes
    /// to a setting.
    /// </summary>
    public double SidebarWidth { get; set; } = 200.0;

    /// <summary>
    /// Positions the overlay on the given monitor, shows it, and becomes
    /// receptive to <see cref="Sync"/> calls. Subsequent <see cref="ShowOn"/>
    /// invocations re-position the existing window (no re-creation).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The overlay's first Show() must never let a WPF resource/rendering failure tear down the caller — log loudly and keep the app interactive. The Plan-02 harness specifically surfaces a crash here."
    )]
    public void ShowOn(IntPtr monitor)
    {
        // Fall back to the primary monitor when MonitorFromWindow returned
        // IntPtr.Zero (happens with detached / minimised / cross-session
        // HWNDs). A null HMONITOR would otherwise propagate into
        // GetMonitorInfo and throw Win32InteropException — the Plan-02
        // harness's "Show Sidebar" button crashed on this path.
        var effectiveMonitor = monitor;
        if (effectiveMonitor == IntPtr.Zero)
        {
            s_logMonitorFallback(_log, monitor, null);
            effectiveMonitor = NativeMethods.MonitorFromWindow(
                IntPtr.Zero,
                NativeMethods.MONITOR_DEFAULTTOPRIMARY
            );
        }

        _monitor = OverlayMonitor.Resolve(effectiveMonitor, _log);
        PositionWindow();

        if (!IsVisible)
        {
            try
            {
                Show();
            }
            catch (Exception ex)
            {
                s_logShowFault(_log, ex);
                throw;
            }
        }

        // Re-run last layout against new geometry (idempotent if no windows).
        try
        {
            RecomputeAndApplyLayout();
        }
        catch (Exception ex)
        {
            s_logSyncFault(_log, ex);
            throw;
        }
    }

    /// <summary>
    /// Disposes every registered thumbnail, then closes the window. Safe to
    /// call repeatedly; the second call is a no-op because <see cref="Close"/>
    /// has already been invoked.
    /// </summary>
    public void HideAndRelease()
    {
        // DWM thumbnail APIs are not thread-safe against arbitrary callers
        // and the overlay Window has dispatcher affinity anyway. VerifyAccess
        // (inherited from DispatcherObject) throws InvalidOperationException
        // when called off the UI thread — exactly the symptom we want to
        // surface loudly rather than risk a DWM corruption.
        VerifyAccess();

        foreach (KeyValuePair<WindowIdentity, DwmThumbnail> pair in _liveThumbnails)
        {
            pair.Value.Dispose();
        }
        _liveThumbnails.Clear();

        Close();
    }

    /// <summary>
    /// Diffs <paramref name="windows"/> against the currently registered
    /// thumbnails: removes vanished entries, registers new ones, then runs
    /// the layout engine and pushes each destination rectangle to DWM.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Sync is driven by harness clicks / hook events; a single badly-behaved HWND must not tear the overlay down. Failures are logged; the caller retries on next refresh."
    )]
    public void Sync(IReadOnlyList<WindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _lastWindows = windows;
        s_logSync(_log, windows.Count, null);

        try
        {
            SyncCore(windows);
        }
        catch (Exception ex)
        {
            s_logSyncFault(_log, ex);
            throw;
        }
    }

    private void SyncCore(IReadOnlyList<WindowSnapshot> windows)
    {
        if (_hwnd == IntPtr.Zero)
        {
            // Window handle not yet realised — the caller invoked Sync before
            // ShowOn (or before SourceInitialized ran). Defer until ShowOn.
            return;
        }

        // 1. Diff: identities currently registered vs. incoming.
        var incomingIds = new HashSet<WindowIdentity>(windows.Count);
        foreach (WindowSnapshot w in windows)
        {
            incomingIds.Add(ToIdentity(w));
        }

        // 2. Remove vanished.
        var toRemove = new List<WindowIdentity>();
        foreach (WindowIdentity existing in _liveThumbnails.Keys)
        {
            if (!incomingIds.Contains(existing))
            {
                toRemove.Add(existing);
            }
        }
        foreach (WindowIdentity id in toRemove)
        {
            _liveThumbnails[id].Dispose();
            _liveThumbnails.Remove(id);
        }

        // 3. Register new.
        foreach (WindowSnapshot w in windows)
        {
            WindowIdentity id = ToIdentity(w);
            if (_liveThumbnails.ContainsKey(id))
            {
                continue;
            }
            // DWM rejects source==destination with E_INVALIDARG; skip the
            // overlay's own HWND if it ever shows up in the input list.
            if (w.Hwnd == _hwnd)
            {
                continue;
            }
            try
            {
                DwmThumbnail thumb = _thumbnails.Register(w.Hwnd, _hwnd);
                _liveThumbnails[id] = thumb;
            }
            catch (Exception ex)
                when (ex is App.Interop.Errors.Win32InteropException or ObjectDisposedException)
            {
                // Win32InteropException: an un-registrable source is logged and
                //   skipped — the rest of the sync proceeds, honouring Plan 02
                //   §Design.8 bullet 2.
                // ObjectDisposedException: the source HWND was destroyed between
                //   enumeration and register — skip; the next Sync picks up the
                //   removal.
                s_logRegisterFailed(_log, w.Hwnd, ex);
            }
        }

        // 4 + 5. Compute layout and push per-placement destination rects.
        RecomputeAndApplyLayout();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyExtendedStyles();

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        base.OnClosed(e);
    }

    private void ApplyExtendedStyles()
    {
        if (_styleApplied || _hwnd == IntPtr.Zero)
        {
            return;
        }

        // Merge WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW into the existing ex-style.
        // Cast through long to avoid the uint -> IntPtr sign-extension surprise on x64.
        var current = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        var merged =
            current.ToInt64()
            | (long)NativeMethods.WS_EX_NOACTIVATE
            | (long)NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(merged));
        _styleApplied = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged)
        {
            s_logDpiChanged(_log, null);
            // Re-resolve the monitor (DPI changed because the overlay moved
            // onto a differently-scaled display), re-position, and re-sync
            // the last window set against the new geometry.
            if (_monitor is { } mon)
            {
                _monitor = OverlayMonitor.Resolve(mon.Handle, _log);
                PositionWindow();
                RecomputeAndApplyLayout();
            }
            // Do not mark handled — WPF's own handler also wants to see it.
        }
        return IntPtr.Zero;
    }

    private void OnSidebarMouseMove(object sender, MouseEventArgs e)
    {
        Point posDip = e.GetPosition(this);
        var hit = HitTest(posDip.X, posDip.Y);
        if (hit is null)
        {
            HoverHighlight.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_lastTileRects.TryGetValue(hit.Value.Primary, out Rect r) || _monitor is not { } mon)
        {
            HoverHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        var scale = mon.DpiScale == 0 ? 1.0 : mon.DpiScale;
        // Convert tile rect (physical pixels) back to DIPs for WPF positioning.
        Canvas.SetLeft(HoverHighlight, r.X / scale);
        Canvas.SetTop(HoverHighlight, r.Y / scale);
        HoverHighlight.Width = r.Width / scale;
        HoverHighlight.Height = r.Height / scale;
        HoverHighlight.Visibility = Visibility.Visible;
    }

    private void OnSidebarMouseLeave(object sender, MouseEventArgs e)
    {
        HoverHighlight.Visibility = Visibility.Collapsed;
    }

    private void OnSidebarLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point posDip = e.GetPosition(this);
        var clickPx = HitTest(posDip.X, posDip.Y);
        if (clickPx is not { } hit)
        {
            return;
        }
        e.Handled = true;
        TileClicked?.Invoke(this, new SidebarTileClickedEventArgs(hit.Scene, hit.Primary));
    }

    private (SceneId Scene, WindowIdentity Primary)? HitTest(double xClientDip, double yClientDip)
    {
        if (_monitor is not { } mon || _sceneTiles.Count == 0)
        {
            return null;
        }
        var scale = mon.DpiScale == 0 ? 1.0 : mon.DpiScale;
        var xPx = (int)Math.Round(xClientDip * scale);
        var yPx = (int)Math.Round(yClientDip * scale);

        foreach (SceneTileEntry tile in _sceneTiles)
        {
            if (
                _lastTileRects.TryGetValue(tile.Primary, out Rect r)
                && xPx >= r.X
                && xPx < r.X + r.Width
                && yPx >= r.Y
                && yPx < r.Y + r.Height
            )
            {
                return (tile.Scene, tile.Primary);
            }
        }
        return null;
    }

    /// <summary>
    /// Scene-aware variant of <see cref="Sync"/>. Each <see cref="Scene"/>
    /// renders as one tile (the scene's primary thumbnail); <see cref="TileClicked"/>
    /// resolves clicks back to the scene id.
    /// </summary>
    public void SyncScenes(IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        _sceneTiles.Clear();
        var synthetic = new List<WindowSnapshot>(scenes.Count);
        // Build a synthetic WindowSnapshot per scene so the existing
        // window-list Sync pipeline can be reused. Filter-related fields
        // (Style, ExStyle, ClassName, ProcessName, IsCloaked, HasOwner)
        // are intentionally zeroed because the layout engine only reads
        // (Identity, Bounds). When Plan 04 adds layout choices that depend
        // on the filter fields (e.g. badge styling for cloaked apps),
        // pipe them through SceneTileEntry instead of rediscovering them.
        foreach (Scene scene in scenes)
        {
            ParkedWindow primaryPw = scene.Windows.First(pw => pw.Identity == scene.Primary);
            synthetic.Add(
                new WindowSnapshot(
                    Hwnd: scene.Primary.Hwnd,
                    Title: scene.Title,
                    ClassName: string.Empty,
                    ProcessId: scene.Primary.ProcessId,
                    ProcessStartTimeUtcTicks: scene.Primary.ProcessStartTimeUtcTicks,
                    Bounds: primaryPw.OriginalBounds,
                    Monitor: IntPtr.Zero,
                    IsVisible: true,
                    IsCloaked: false,
                    IsTopLevel: true,
                    Style: 0,
                    ExStyle: 0,
                    HasOwner: false,
                    ProcessName: string.Empty
                )
            );
            _sceneTiles.Add(new SceneTileEntry(scene.Id, scene.Primary, scene.Windows.Count));
        }

        Sync(synthetic);
    }

    private void PositionWindow()
    {
        if (_monitor is not { } mon)
        {
            return;
        }

        // Convert physical-pixel work area into DIPs for WPF.
        var scale = mon.DpiScale == 0 ? 1.0 : mon.DpiScale;
        var workLeftDip = mon.WorkArea.X / scale;
        var workTopDip = mon.WorkArea.Y / scale;
        var workWidthDip = mon.WorkArea.Width / scale;
        var workHeightDip = mon.WorkArea.Height / scale;

        var xDip =
            Edge == SidebarEdge.Left ? workLeftDip : workLeftDip + workWidthDip - SidebarWidth;

        Left = xDip;
        Top = workTopDip;
        Width = SidebarWidth;
        Height = workHeightDip;
    }

    private void RecomputeAndApplyLayout()
    {
        if (_hwnd == IntPtr.Zero || _monitor is not { } mon || _liveThumbnails.Count == 0)
        {
            return;
        }

        var scale = mon.DpiScale == 0 ? 1.0 : mon.DpiScale;

        // Sidebar rect passed to the layout engine is in physical pixels and
        // local to the overlay's client area (origin 0,0). DWM thumbnail
        // destination rects are also in physical pixels relative to the
        // destination HWND's client area — same coordinate system, no shift.
        var widthPx = (int)Math.Round(ActualWidth * scale);
        var heightPx = (int)Math.Round(ActualHeight * scale);
        if (widthPx <= 0 || heightPx <= 0)
        {
            // Window not fully measured yet (happens when Sync arrives before
            // the first layout pass). The post-ShowOn RecomputeAndApply call
            // will catch up.
            return;
        }

        var sidebarBounds = new Rect(0, 0, widthPx, heightPx);

        // Build the layout input, preserving the order of the last Sync.
        var windowsInput = new List<(WindowIdentity Identity, CoreSize SourceSize)>(
            _lastWindows.Count
        );
        foreach (WindowSnapshot snap in _lastWindows)
        {
            WindowIdentity id = ToIdentity(snap);
            if (!_liveThumbnails.TryGetValue(id, out DwmThumbnail? thumb))
            {
                continue;
            }
            // DwmThumbnail.SourceSize is 0 until the first UpdateDestinationRect
            // — seed with the snapshot bounds (also physical pixels) so the
            // first layout pass has a sensible aspect ratio.
            CoreSize src = thumb.SourceSize.IsEmpty
                ? new CoreSize(Math.Max(1, snap.Bounds.Width), Math.Max(1, snap.Bounds.Height))
                : new CoreSize(thumb.SourceSize.Width, thumb.SourceSize.Height);
            windowsInput.Add((id, src));
        }

        var request = new LayoutRequest(
            sidebarBounds,
            DefaultThumbnailSpacing * scale,
            DefaultOuterPadding * scale,
            DefaultMaxThumbnailHeight * scale,
            windowsInput
        );

        IReadOnlyList<ThumbnailPlacement> placements = _layout.Compute(request);
        _lastTileRects.Clear();
        foreach (ThumbnailPlacement placement in placements)
        {
            if (_liveThumbnails.TryGetValue(placement.Identity, out DwmThumbnail? thumb))
            {
                thumb.UpdateDestinationRect(placement.DestinationRect);
                _lastTileRects[placement.Identity] = placement.DestinationRect;
            }
        }
    }

    private static WindowIdentity ToIdentity(WindowSnapshot w) =>
        new(w.Hwnd, w.ProcessId, w.ProcessStartTimeUtcTicks);

    // The App.Interop.Size type has a property named IsEmpty — see Size.cs.
}

/// <summary>Payload for <see cref="SidebarOverlay.TileClicked"/>.</summary>
/// <param name="Scene">The scene whose tile was clicked.</param>
/// <param name="Primary">The clicked tile's primary window identity.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Matches Plan-03 spec; the type is an event payload."
)]
public sealed record SidebarTileClickedEventArgs(SceneId Scene, WindowIdentity Primary);
