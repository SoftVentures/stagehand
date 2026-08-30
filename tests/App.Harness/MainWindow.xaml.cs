using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using App.Core.Stage;
using App.Interop;
using App.Services.Windows;
using App.Shell.Overlay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rect = App.Interop.Rect;

namespace App.Harness;

/// <summary>
/// Plan 02 §S9 harness main window. Wires up the buttons required by the
/// Plan 02 acceptance-criteria manual checklist.
/// </summary>
/// <remarks>
/// <para>
/// Services are pulled from the <see cref="HarnessApp"/>'s root
/// <see cref="IServiceProvider"/>. The harness is single-window, single-use;
/// there is no need for a DI scope per interaction.
/// </para>
/// <para>
/// <b>Sidebar lifecycle.</b> "Show Sidebar" resolves a fresh
/// <see cref="SidebarOverlay"/> from DI each time the user toggles the
/// button on: WPF's <see cref="Window.Close"/> (invoked by the previous
/// <c>HideAndRelease</c>) permanently disposes the window's native peer, so
/// re-showing the same instance is not supported.
/// </para>
/// <para>
/// <b>Resource counter.</b> A 1 Hz <see cref="DispatcherTimer"/> polls
/// <c>GetGuiResources</c> for the harness process — the Plan 02 §Acceptance
/// Criteria soak test watches these for leaks over a 10-minute run.
/// </para>
/// <para>
/// <b>Defensive handlers.</b> Every button handler is wrapped in a
/// <c>try/catch</c> that logs to the in-window <c>HookLog</c>, the
/// <see cref="ILogger{TCategoryName}"/> (console + file), and swallows the
/// exception so the harness remains interactive. The Plan-02 checklist
/// discovered that an un-caught exception inside <c>OnLoaded</c> made the
/// window appear inert (no listbox content, no visible error).
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logParkFailed =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(6001, "HarnessParkFailed"),
            "Park failed for HWND 0x{Hwnd:X}."
        );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logRestoreFailed =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(6002, "HarnessRestoreFailed"),
            "Restore failed for HWND 0x{Hwnd:X}."
        );

    private static readonly Action<ILogger, string, Exception?> s_logHandlerFault =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(6003, "HarnessHandlerFault"),
            "Harness handler {Handler} threw."
        );

    private static readonly Action<ILogger, IntPtr, int, Exception?> s_logKillFailed =
        LoggerMessage.Define<IntPtr, int>(
            LogLevel.Warning,
            new EventId(6004, "HarnessKillFailed"),
            "Kill failed for HWND 0x{Hwnd:X} (PID {ProcessId})."
        );

    private readonly IServiceProvider _services;
    private readonly WindowListViewModel _viewModel;
    private readonly IWindowController _controller;
    private readonly IWinEventHookFactory _hookFactory;
    private readonly ILogger<MainWindow> _log;
    private readonly IntPtr _currentProcessHandle;
    private readonly DispatcherTimer _resourceTimer;

    private SidebarOverlay? _sidebar;
    private WinEventHook? _winEventHook;

    // Additional hooks kept alive for disposal on window close. Separate from
    // _winEventHook so the "already registered" guard has a single flag.
    private readonly System.Collections.Generic.List<WinEventHook> _compositeHooks = [];

    public MainWindow()
        : this(((HarnessApp)Application.Current).Services) { }

    internal MainWindow(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        _viewModel = new WindowListViewModel(
            services.GetRequiredService<IManageableWindowService>(),
            services.GetRequiredService<IBitmapThumbnailFactory>(),
            services.GetRequiredService<ILogger<WindowListViewModel>>()
        );
        _controller = services.GetRequiredService<IWindowController>();
        _hookFactory = services.GetRequiredService<IWinEventHookFactory>();
        _log = services.GetRequiredService<ILogger<MainWindow>>();
        _currentProcessHandle = Process.GetCurrentProcess().Handle;

        InitializeComponent();
        WindowList.ItemsSource = _viewModel.Windows;

        _resourceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _resourceTimer.Tick += (_, _) =>
            SafeInvoke(nameof(UpdateResourceCounters), UpdateResourceCounters);

        Loaded += OnLoaded;
        Closed += OnWindowClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnLoaded),
            () =>
            {
                // Seed the list on open so the user sees state immediately.
                IReadOnlyList<WindowSnapshot> snapshots = _viewModel.Refresh();
                AppendHookLog($"Initial refresh: {snapshots.Count} manageable window(s).");
                UpdateResourceCounters();
                _resourceTimer.Start();
            }
        );

    private void OnWindowClosed(object? sender, EventArgs e) =>
        SafeInvoke(
            nameof(OnWindowClosed),
            () =>
            {
                _resourceTimer.Stop();
                _winEventHook?.Dispose();
                _winEventHook = null;
                foreach (WinEventHook hook in _compositeHooks)
                {
                    hook.Dispose();
                }
                _compositeHooks.Clear();
                _sidebar?.HideAndRelease();
                _sidebar = null;
            }
        );

    // ---------- Button handlers ----------

    private void OnRefreshClicked(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnRefreshClicked),
            () =>
            {
                IReadOnlyList<WindowSnapshot> snapshots = _viewModel.Refresh();
                AppendHookLog($"Refresh: {snapshots.Count} manageable window(s).");
                // If the sidebar is currently visible, push the new list through its
                // Sync pipeline so thumbnails reflect the refresh result.
                _sidebar?.Sync(snapshots);
            }
        );

    private void OnShowSidebarClicked(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnShowSidebarClicked),
            () =>
            {
                if (_sidebar is not null)
                {
                    // Toggle off.
                    _sidebar.HideAndRelease();
                    _sidebar = null;
                    ShowSidebarButton.Content = "Show Sidebar";
                    AppendHookLog("Sidebar hidden.");
                    return;
                }

                // Resolve a fresh overlay from DI (transient registration in HarnessApp).
                _sidebar = _services.GetRequiredService<SidebarOverlay>();

                // Anchor on the monitor currently hosting this harness window.
                var hwnd = new WindowInteropHelper(this).Handle;
                var monitor = NativeMethods.MonitorFromWindow(
                    hwnd,
                    NativeMethods.MONITOR_DEFAULTTOPRIMARY
                );
                _sidebar.ShowOn(monitor);
                _sidebar.Sync(_viewModel.Refresh());
                ShowSidebarButton.Content = "Hide Sidebar";
                AppendHookLog($"Sidebar shown on monitor=0x{monitor:X}.");
            }
        );

    private void OnParkClicked(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnParkClicked),
            () =>
            {
                if (WindowList.SelectedItem is not WindowListItem item)
                {
                    AppendHookLog("Park skipped — no selection.");
                    return;
                }
                WindowSnapshot snap = item.Snapshot;

                // Cache the original bounds so "Restore" has somewhere to put it back.
                _viewModel.ParkedOriginalBounds[snap.Hwnd] = snap.Bounds;

                // Park off-screen at the canonical (-32000, -32000) slot while
                // preserving the original size. WindowController applies the
                // NOZORDER | NOACTIVATE | NOREDRAW flags internally.
                var parkingRect = new Rect(-32000, -32000, snap.Bounds.Width, snap.Bounds.Height);
                try
                {
                    _controller.Park(snap.Hwnd, parkingRect);
                    AppendHookLog($"Park: hwnd=0x{snap.Hwnd:X} title='{snap.Title}'");
                }
                catch (Exception ex)
                {
                    AppendHookLog($"Park FAILED: {ex.GetType().Name}: {ex.Message}");
                    s_logParkFailed(_log, snap.Hwnd, ex);
                }
            }
        );

    private void OnRestoreClicked(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnRestoreClicked),
            () =>
            {
                if (WindowList.SelectedItem is not WindowListItem item)
                {
                    AppendHookLog("Restore skipped — no selection.");
                    return;
                }
                WindowSnapshot snap = item.Snapshot;
                if (
                    !_viewModel.ParkedOriginalBounds.TryGetValue(snap.Hwnd, out Rect originalBounds)
                )
                {
                    AppendHookLog($"Restore skipped — no cached bounds for hwnd=0x{snap.Hwnd:X}");
                    return;
                }

                try
                {
                    _controller.RestorePosition(snap.Hwnd, originalBounds);
                    _ = _viewModel.ParkedOriginalBounds.Remove(snap.Hwnd);
                    AppendHookLog($"Restore: hwnd=0x{snap.Hwnd:X} → {originalBounds}");
                }
                catch (Exception ex)
                {
                    AppendHookLog($"Restore FAILED: {ex.GetType().Name}: {ex.Message}");
                    s_logRestoreFailed(_log, snap.Hwnd, ex);
                }
            }
        );

    private void OnKillClicked(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnKillClicked),
            () =>
            {
                if (WindowList.SelectedItem is not WindowListItem item)
                {
                    AppendHookLog("Kill skipped — no selection.");
                    return;
                }
                WindowSnapshot snap = item.Snapshot;
                int pid = snap.ProcessId;

                try
                {
                    using Process proc = Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                    AppendHookLog($"Kill: hwnd=0x{snap.Hwnd:X} title='{snap.Title}' pid={pid}");
                    // Drop any cached park bounds for the now-dead HWND so a
                    // stale Restore click does not try to move it.
                    _ = _viewModel.ParkedOriginalBounds.Remove(snap.Hwnd);
                    // Refresh re-enumerates and re-captures previews so the
                    // killed window vanishes from the list immediately. Keep
                    // a single enumeration result and reuse it for the sidebar
                    // sync so we don't pay the capture cost twice.
                    IReadOnlyList<WindowSnapshot> after = _viewModel.Refresh();
                    _sidebar?.Sync(after);
                }
                catch (Exception ex)
                {
                    AppendHookLog($"Kill FAILED: {ex.GetType().Name}: {ex.Message}");
                    s_logKillFailed(_log, snap.Hwnd, pid, ex);
                }
            }
        );

    private void OnRegisterHookClicked(object sender, RoutedEventArgs e) =>
        SafeInvoke(
            nameof(OnRegisterHookClicked),
            () =>
            {
                if (_winEventHook is not null)
                {
                    AppendHookLog("Hook already registered — ignoring click.");
                    return;
                }

                // Plan 02 §Design.9: create / destroy / foreground is enough to
                // prove the hook thread + dispatcher marshalling work.
                // EVENT_OBJECT_CREATE=0x8000, EVENT_OBJECT_DESTROY=0x8001,
                // EVENT_SYSTEM_FOREGROUND=0x0003. The min/max window has to span
                // foreground..destroy, so we install two hooks — one for the low
                // event id (foreground) and one for the high range (create/destroy).
                var foregroundSpec = new WinEventSpec(EventMin: 0x0003, EventMax: 0x0003);
                var objectSpec = new WinEventSpec(EventMin: 0x8000, EventMax: 0x8001);

                WinEventHook fg = _hookFactory.Create(foregroundSpec);
                WinEventHook obj = _hookFactory.Create(objectSpec);

                fg.Fired += (_, args) => OnHookFired("FG", args);
                obj.Fired += (_, args) => OnHookFired("OBJ", args);

                // Keep the foreground one as the disposable anchor; we wrap both in
                // a composite to dispose cleanly on window close.
                _winEventHook = fg;
                _compositeHooks.Add(obj);

                RegisterHookButton.IsEnabled = false;
                AppendHookLog("Hook registered (FOREGROUND + CREATE/DESTROY).");
            }
        );

    private async void OnEnableStageClicked(object sender, RoutedEventArgs e)
    {
        HarnessTrace.Write("OnEnableStageClicked: entry");
        try
        {
            IStageController stage = _services.GetRequiredService<IStageController>();
            HarnessTrace.Write($"OnEnableStageClicked: phase={stage.CurrentState.Phase}");
            AppendHookLog($"Enable Stage… (phase={stage.CurrentState.Phase})");
            HarnessTrace.Write("OnEnableStageClicked: awaiting EnableAsync");
            await stage.EnableAsync(System.Threading.CancellationToken.None).ConfigureAwait(true);
            HarnessTrace.Write($"OnEnableStageClicked: returned, phase={stage.CurrentState.Phase}");
            AppendHookLog($"Stage enabled. Phase={stage.CurrentState.Phase}");
        }
        catch (Exception ex)
        {
            HarnessTrace.Write(
                $"OnEnableStageClicked: THREW {ex.GetType().Name}: {ex.Message}\n{ex}"
            );
            AppendHookLog($"Enable FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnDisableStageClicked(object sender, RoutedEventArgs e)
    {
        HarnessTrace.Write("OnDisableStageClicked: entry");
        try
        {
            IStageController stage = _services.GetRequiredService<IStageController>();
            HarnessTrace.Write($"OnDisableStageClicked: phase={stage.CurrentState.Phase}");
            AppendHookLog($"Disable Stage… (phase={stage.CurrentState.Phase})");
            HarnessTrace.Write("OnDisableStageClicked: awaiting DisableAsync");
            await stage.DisableAsync(System.Threading.CancellationToken.None).ConfigureAwait(true);
            HarnessTrace.Write(
                $"OnDisableStageClicked: returned, phase={stage.CurrentState.Phase}"
            );
            AppendHookLog($"Stage disabled. Phase={stage.CurrentState.Phase}");
        }
        catch (Exception ex)
        {
            HarnessTrace.Write(
                $"OnDisableStageClicked: THREW {ex.GetType().Name}: {ex.Message}\n{ex}"
            );
            AppendHookLog($"Disable FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SafeInvoke(string handler, Func<Task> asyncAction) =>
        SafeInvoke(handler, () => _ = SafeAsync(handler, asyncAction));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Harness handler isolation; same rationale as the synchronous SafeInvoke."
    )]
    private async Task SafeAsync(string handler, Func<Task> asyncAction)
    {
        try
        {
            await asyncAction().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendHookLog($"ERROR in {handler}: {ex.GetType().Name}: {ex.Message}");
            s_logHandlerFault(_log, handler, ex);
        }
    }

    private void OnHookFired(string tag, WinEventArgs args) =>
        SafeInvoke(
            nameof(OnHookFired),
            () =>
            {
                // Arrives on the UI thread (WinEventHook.Fired is marshalled through
                // UiDispatcher.Post). Safe to touch WPF UI here.
                AppendHookLog(
                    $"[{tag}] event=0x{args.EventId:X4} hwnd=0x{args.Hwnd:X} tid={args.Thread} t={args.Time}"
                );
            }
        );

    // ---------- UI helpers ----------

    private void OnWindowSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SafeInvoke(
            nameof(OnWindowSelectionChanged),
            () =>
            {
                var hasSelection = WindowList.SelectedItem is WindowListItem;
                ParkButton.IsEnabled = hasSelection;
                RestoreButton.IsEnabled = hasSelection;
                KillButton.IsEnabled = hasSelection;
            }
        );

    private void AppendHookLog(string line)
    {
        // Cap log size so a long-running session doesn't OOM the TextBox.
        const int MaxChars = 64 * 1024;
        HookLog.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}");
        if (HookLog.Text.Length > MaxChars)
        {
            HookLog.Text = HookLog.Text[^MaxChars..];
        }
        HookLog.ScrollToEnd();
    }

    private void UpdateResourceCounters()
    {
        var gdi = NativeMethods.GetGuiResources(_currentProcessHandle, NativeMethods.GR_GDIOBJECTS);
        var usr = NativeMethods.GetGuiResources(
            _currentProcessHandle,
            NativeMethods.GR_USEROBJECTS
        );
        GdiObjectsText.Text = $"GDI objects: {gdi}";
        UserObjectsText.Text = $"User objects: {usr}";
    }

    /// <summary>
    /// Runs <paramref name="action"/> under a blanket try/catch that logs
    /// the failure to the on-screen <c>HookLog</c>, the <see cref="ILogger"/>
    /// (console + file sinks), and swallows the exception so the WPF
    /// dispatcher does not tear the app down. Applied to every button
    /// handler after the Plan-02 checklist found silent failures.
    /// </summary>
#pragma warning disable CA1031 // General catch is exactly the harness's purpose here.
    private void SafeInvoke(string handler, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                AppendHookLog($"ERROR in {handler}: {ex.GetType().Name}: {ex.Message}");
            }
            catch
            {
                // If even the log write fails (UI torn down?) there's nothing sane to do.
            }
            s_logHandlerFault(_log, handler, ex);
        }
    }
#pragma warning restore CA1031
}
