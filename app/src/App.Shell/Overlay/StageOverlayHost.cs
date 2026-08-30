using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using App.Core.Stage;
using App.Interop;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Shell.Overlay;

/// <summary>
/// Production <see cref="IStageOverlayHost"/>. Owns one
/// <see cref="SidebarOverlay"/> per managed HMONITOR and forwards
/// per-overlay tile-click events as <see cref="IStageOverlayHost.SceneClicked"/>
/// with the source monitor's GDI device name resolved via
/// <see cref="MonitorEnumerator"/>.
/// </summary>
/// <remarks>
/// All overlay lifecycle work (Create / Dispose / Sync) marshals to the
/// UI thread via <see cref="UiDispatcher"/>; the controller may call from
/// any thread.
/// </remarks>
public sealed class StageOverlayHost : IStageOverlayHost, IDisposable
{
    private static readonly Action<ILogger, IntPtr, Exception?> s_logCreate =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Debug,
            new EventId(8001, nameof(StageOverlayHost) + ".Create"),
            "StageOverlayHost: created overlay for HMONITOR 0x{HMonitor:X}."
        );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logDispose =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Debug,
            new EventId(8002, nameof(StageOverlayHost) + ".Dispose"),
            "StageOverlayHost: disposed overlay for HMONITOR 0x{HMonitor:X}."
        );

    private static readonly Action<ILogger, IntPtr, Exception?> s_logSyncMissing =
        LoggerMessage.Define<IntPtr>(
            LogLevel.Warning,
            new EventId(8003, nameof(StageOverlayHost) + ".SyncMissing"),
            "StageOverlayHost: SyncMonitor for HMONITOR 0x{HMonitor:X} but no overlay exists."
        );

    private static readonly Action<ILogger, Exception?> s_logResolveFault = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(8004, nameof(StageOverlayHost) + ".ResolveFault"),
        "StageOverlayHost: failed to resolve clicked HMONITOR to a device name; SceneClicked dropped."
    );

    private readonly Func<ISidebarOverlayHandle> _factory;
    private readonly UiDispatcher _ui;
    private readonly MonitorEnumerator _monitors;
    private readonly ILogger<StageOverlayHost> _log;
    private readonly ConcurrentDictionary<IntPtr, ISidebarOverlayHandle> _overlays = new();

    /// <summary>Production constructor.</summary>
    public StageOverlayHost(
        Func<ISidebarOverlayHandle> factory,
        UiDispatcher ui,
        MonitorEnumerator monitors,
        ILogger<StageOverlayHost> log
    )
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public IReadOnlyList<IntPtr> ManagedMonitors => _overlays.Keys.ToList();

    /// <inheritdoc />
    public event EventHandler<SceneClickedEventArgs>? SceneClicked;

    /// <inheritdoc />
    public void CreateForMonitor(IntPtr monitor)
    {
        _ui.Invoke(() =>
        {
            if (_overlays.ContainsKey(monitor))
            {
                return;
            }
            ISidebarOverlayHandle overlay = _factory();
            overlay.TileClicked += (_, args) => OnTileClicked(monitor, args);
            overlay.ShowOn(monitor);
            _overlays[monitor] = overlay;
            s_logCreate(_log, monitor, null);
        });
    }

    /// <inheritdoc />
    public void DisposeForMonitor(IntPtr monitor)
    {
        _ui.Invoke(() =>
        {
            if (_overlays.TryRemove(monitor, out ISidebarOverlayHandle? overlay))
            {
                try
                {
                    overlay.HideAndRelease();
                }
                catch (InvalidOperationException)
                {
                    // overlay already closed; ignore
                }
                s_logDispose(_log, monitor, null);
            }
        });
    }

    /// <inheritdoc />
    public void SyncMonitor(IntPtr monitor, IReadOnlyList<Scene> scenes)
    {
        _ui.Invoke(() =>
        {
            if (!_overlays.TryGetValue(monitor, out ISidebarOverlayHandle? overlay))
            {
                s_logSyncMissing(_log, monitor, null);
                return;
            }
            overlay.SyncScenes(scenes);
        });
    }

    /// <inheritdoc />
    public void DisposeAll()
    {
        _ui.Invoke(() =>
        {
            foreach (KeyValuePair<IntPtr, ISidebarOverlayHandle> pair in _overlays.ToArray())
            {
                if (_overlays.TryRemove(pair.Key, out ISidebarOverlayHandle? overlay))
                {
                    try
                    {
                        overlay.HideAndRelease();
                    }
                    catch (InvalidOperationException)
                    {
                        // ignore
                    }
                }
            }
        });
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA1816:Dispose methods should call SuppressFinalize",
        Justification = "Sealed class with no finalizer."
    )]
    public void Dispose() => DisposeAll();

    private void OnTileClicked(IntPtr monitor, SidebarTileClickedEventArgs args)
    {
        // Resolve hmonitor → device name. May fail if the monitor was
        // detached between rendering and the click — treat as a soft error.
        MonitorDescriptor? descriptor = null;
        foreach (MonitorDescriptor m in _monitors.EnumerateAll())
        {
            if (m.Hmonitor == monitor)
            {
                descriptor = m;
                break;
            }
        }
        if (descriptor is not { } d)
        {
            s_logResolveFault(_log, null);
            return;
        }
        SceneClicked?.Invoke(
            this,
            new SceneClickedEventArgs(args.Scene, args.Primary, d.DeviceName)
        );
    }
}
