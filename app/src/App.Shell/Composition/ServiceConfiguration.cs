using System;
using App.Core.Layout;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Interop.Threading;
using App.Services.Hotkeys;
using App.Services.Logging;
using App.Services.Settings;
using App.Services.Updates;
using App.Services.Windows;
using App.Shell.Interaction;
using App.Shell.Monitors;
using App.Shell.Overlay;
using App.Shell.Recovery;
using App.Shell.Settings;
using App.Shell.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Shell.Composition;

/// <summary>
/// Composition-root helpers that register Stagehand's services on an
/// <see cref="IServiceCollection"/> and configure logging via
/// <see cref="LoggingConfiguration.AddStagehandLogging"/>.
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Registers core, services, interop stubs, and UI-layer singletons.
    /// Callers must register <see cref="ISettingsPathProvider"/> before invoking
    /// this method so logging can be wired to the correct directory.
    /// </summary>
    public static IServiceCollection ConfigureStagehand(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Clock (settings depend on it).
        services.AddSingleton<IClock, SystemClock>();

        // UI-thread marshalling seam. The Dispatcher instance is supplied by
        // the caller (ShellApp.OnStartup) so we never reach into
        // Application.Current from the DI module — that keeps the registration
        // testable and avoids an ambient static dependency.
        services.AddSingleton<UiDispatcher>(_ => new UiDispatcher(
            System.Windows.Application.Current.Dispatcher
        ));

        // Settings (real).
        services.AddSingleton<SettingsMigrator>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Plan 02 window mechanics.
        services.AddSingleton<IWindowEnumerator, WindowEnumerator>();
        services.AddSingleton<IWindowController, WindowController>();
        services.AddSingleton<IWindowFilter, WindowFilter>();
        services.AddSingleton<IManageableWindowService, ManageableWindowService>();
        services.AddSingleton<IDwmThumbnailFactory, DwmThumbnailFactory>();
        services.AddSingleton<IBitmapThumbnailFactory, BitmapThumbnailFactory>();
        services.AddSingleton<IThumbnailSourceFactory, ThumbnailSourceFactory>();
        services.AddSingleton<IThumbnailLayoutEngine, ThumbnailLayoutEngine>();
        services.AddSingleton<WinEventHookThread>();
        services.AddSingleton<IWinEventHookFactory, WinEventHookFactory>();

        // Plan 03 multi-monitor + work-area.
        services.AddSingleton<MonitorEnumerator>();
        services.AddSingleton<IWorkAreaManager, WorkAreaManager>();

        // Plan 03 stage controller + scene infrastructure.
        services.AddSingleton<ISceneGrouper, App.Services.Stage.SceneGrouper>();
        services.AddSingleton<ISceneSwapExecutor, InstantSceneSwapExecutor>();
        // SidebarOverlay is transient (one per monitor); the host resolves
        // a fresh ISidebarOverlayHandle (the WPF window implements it) per
        // CreateForMonitor call via this factory.
        services.AddTransient<SidebarOverlay>();
        services.AddSingleton<Func<ISidebarOverlayHandle>>(sp =>
            () => sp.GetRequiredService<SidebarOverlay>()
        );
        services.AddSingleton<IStageOverlayHost, StageOverlayHost>();
        // Snapshot store + path provider for crash recovery.
        services.AddSingleton<
            App.Services.Snapshot.ISnapshotPathProvider,
            App.Services.Snapshot.AppDataSnapshotPathProvider
        >();
        services.AddSingleton<App.Services.Snapshot.SnapshotStore>();
        services.AddSingleton<ISnapshotStore>(sp =>
            sp.GetRequiredService<App.Services.Snapshot.SnapshotStore>()
        );
        services.AddSingleton<App.Services.Snapshot.ISnapshotReader>(sp =>
            sp.GetRequiredService<App.Services.Snapshot.SnapshotStore>()
        );
        services.AddSingleton<IStageController, StageController>();
        services.AddSingleton<StageInteractionCoordinator>();
        services.AddSingleton<DisplayChangeListener>();
        services.AddSingleton<MonitorChangeCoordinator>();
        services.AddSingleton<CrashRecoveryCoordinator>();

        // Stubs targeting later plans.
        services.AddSingleton<IStageLayoutEngine, NotImplementedStageLayoutEngine>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IUpdateService, StubUpdateService>();

        // UI-layer services.
        // TrayIconHost is a long-lived singleton; SettingsWindow is transient so
        // each "Settings…" click gets a fresh Window instance. The Func<T>
        // factory resolves from the root provider, which is fine today because
        // SettingsWindow has no IDisposable dependencies. When Plan 04 adds
        // disposable VMs or scoped services (Mica probe caches, per-window
        // animation controllers, etc.), switch to an IServiceScopeFactory-based
        // factory so each window owns a scope that releases on close.
        services.AddSingleton<TrayIconHost>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<Func<SettingsWindow>>(sp =>
            () => sp.GetRequiredService<SettingsWindow>()
        );

        return services;
    }

    /// <summary>
    /// Configures logging providers using the supplied path provider's log
    /// directory. Kept as a separate method so callers can run it inside
    /// <see cref="IServiceCollection.AddLogging(Action{ILoggingBuilder})"/>.
    /// </summary>
    public static ILoggingBuilder ConfigureStagehandLogging(
        this ILoggingBuilder builder,
        ISettingsPathProvider paths
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(paths);
        return builder.AddStagehandLogging(paths.LogDirectoryPath);
    }
}
