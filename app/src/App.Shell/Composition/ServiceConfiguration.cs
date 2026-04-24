using System;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Services.Hotkeys;
using App.Services.Logging;
using App.Services.Settings;
using App.Services.Updates;
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

        // Settings (real).
        services.AddSingleton<SettingsMigrator>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Interop + core stubs targeting Plans 02-05.
        services.AddSingleton<IWindowEnumerator, NotImplementedWindowEnumerator>();
        services.AddSingleton<IWindowController, NotImplementedWindowController>();
        services.AddSingleton<IWorkAreaManager, NotImplementedWorkAreaManager>();
        services.AddSingleton<IWinEventHook, NotImplementedWinEventHook>();
        services.AddSingleton<IStageController, NotImplementedStageController>();
        services.AddSingleton<IStageLayoutEngine, NotImplementedStageLayoutEngine>();
        services.AddSingleton<IWindowFilter, NotImplementedWindowFilter>();
        services.AddSingleton<IHotkeyService, StubHotkeyService>();
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
