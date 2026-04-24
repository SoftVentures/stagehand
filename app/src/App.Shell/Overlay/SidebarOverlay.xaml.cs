using System.Collections.Generic;
using System.Windows;
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
public sealed partial class SidebarOverlay : Window
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

    private readonly IDwmThumbnailFactory _thumbnails;
    private readonly IThumbnailLayoutEngine _layout;
    private readonly ILogger<SidebarOverlay> _log;

    private readonly Dictionary<WindowIdentity, DwmThumbnail> _liveThumbnails = [];
    private IReadOnlyList<WindowSnapshot> _lastWindows = [];
    private OverlayMonitor? _monitor;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _styleApplied;

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
    public void ShowOn(IntPtr monitor)
    {
        _monitor = OverlayMonitor.Resolve(monitor, _log);
        PositionWindow();

        if (!IsVisible)
        {
            Show();
        }

        // Re-run last layout against new geometry (idempotent if no windows).
        RecomputeAndApplyLayout();
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
    public void Sync(IReadOnlyList<WindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _lastWindows = windows;
        s_logSync(_log, windows.Count, null);

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
        foreach (ThumbnailPlacement placement in placements)
        {
            if (_liveThumbnails.TryGetValue(placement.Identity, out DwmThumbnail? thumb))
            {
                thumb.UpdateDestinationRect(placement.DestinationRect);
            }
        }
    }

    private static WindowIdentity ToIdentity(WindowSnapshot w) =>
        new(w.Hwnd, w.ProcessId, w.ProcessStartTimeUtcTicks);

    // The App.Interop.Size type has a property named IsEmpty — see Size.cs.
}
