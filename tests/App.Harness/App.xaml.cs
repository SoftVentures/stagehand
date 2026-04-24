using System;
using System.Windows;
using App.Core.Layout;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Interop.Threading;
using App.Services.Settings;
using App.Services.Windows;
using App.Shell.Overlay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Harness;

/// <summary>
/// Application class for the Plan 02 §S9 WPF harness.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>HarnessApp</c> (not <c>App</c>) for the same reason
/// <see cref="App.Shell.ShellApp"/> avoids the name <c>App</c>: inside a
/// project whose root namespace is <c>App.Harness</c>, a class named
/// <c>App</c> collides with the <c>App</c> namespace segment during C# name
/// resolution in the WPF-generated <c>App.g.cs</c>. See
/// <c>CLAUDE.md</c> §Non-obvious gotchas.
/// </para>
/// <para>
/// <b>Composition strategy.</b> The harness intentionally does NOT call
/// <see cref="App.Shell.Composition.ServiceConfiguration.ConfigureStagehand"/>:
/// that extension registers <c>TrayIconHost</c> and <c>SettingsWindow</c>,
/// which pulls the brand-asset tray icon + settings window graph into the
/// harness. We only need the Plan 02 Wave-1/2 seams (enumerator, filter,
/// controller, thumbnails, layout engine, hook infrastructure) so we wire
/// them here by hand and keep the harness's startup cost minimal.
/// </para>
/// </remarks>
public partial class HarnessApp : Application
{
    private ServiceProvider? _services;

    /// <summary>The DI container the harness's window resolves services from.</summary>
    internal IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("OnStartup has not run yet.");

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureHarness(services);

        _services = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Registers the minimum service graph the Plan 02 harness needs to
    /// exercise every Wave-1/2 seam. No tray icon, no settings window —
    /// the harness's <see cref="MainWindow"/> drives the scenarios directly.
    /// </summary>
    private static void ConfigureHarness(IServiceCollection services)
    {
        // Logging: console-only for the harness. Serilog/file logging would
        // be overkill for a hand-driven run.
        services.AddLogging(builder =>
        {
            _ = builder.AddFilter(level => level >= LogLevel.Debug);
            _ = builder.AddDebug();
        });

        // UI-thread marshalling seam. Application.Current.Dispatcher points
        // at this HarnessApp's dispatcher because we're in its OnStartup chain.
        services.AddSingleton<UiDispatcher>(_ => new UiDispatcher(Current.Dispatcher));

        // Clock (SettingsService depends on it for last-changed timestamps).
        services.AddSingleton<IClock, SystemClock>();

        // Settings — the harness writes no user data; a fixed in-memory
        // path provider keeps the real AppData folder untouched. Registered
        // via an explicit factory so CA1812 doesn't flag the private type.
        services.AddSingleton<ISettingsPathProvider>(_ => new HarnessSettingsPathProvider());
        services.AddSingleton<SettingsMigrator>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Plan 02 seams.
        services.AddSingleton<IWindowEnumerator, WindowEnumerator>();
        services.AddSingleton<IWindowController, WindowController>();
        services.AddSingleton<IWindowFilter, WindowFilter>();
        services.AddSingleton<IManageableWindowService, ManageableWindowService>();
        services.AddSingleton<IDwmThumbnailFactory, DwmThumbnailFactory>();
        services.AddSingleton<IThumbnailLayoutEngine, ThumbnailLayoutEngine>();
        services.AddSingleton<WinEventHookThread>();
        services.AddSingleton<IWinEventHookFactory, WinEventHookFactory>();

        // Overlay is resolved via a transient factory so "Show Sidebar" can
        // recreate it after a HideAndRelease (which calls Window.Close and
        // prevents re-show on the same instance).
        services.AddTransient<SidebarOverlay>();
    }

    /// <summary>
    /// In-memory path provider — never touches <c>%APPDATA%</c>. The harness
    /// still instantiates <see cref="SettingsService"/> so <c>WindowFilter</c>
    /// can subscribe to <c>Changed</c>, but no writes happen during a hand-
    /// driven session.
    /// </summary>
    private sealed class HarnessSettingsPathProvider : ISettingsPathProvider
    {
        public string SettingsFilePath { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stagehand-harness-settings.json");

        public string LogDirectoryPath { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stagehand-harness-logs");
    }
}
