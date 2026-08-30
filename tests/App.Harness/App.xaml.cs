using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using App.Core.Layout;
using App.Core.Stage;
using App.Core.Time;
using App.Interop;
using App.Interop.Threading;
using App.Services.Settings;
using App.Services.Windows;
using App.Shell.Interaction;
using App.Shell.Overlay;
using App.Shell.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

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
/// <para>
/// <b>Diagnostics.</b> The harness installs a full logger fan-out
/// (Debug sink for IDEs, <see cref="Console"/> via <c>AddSimpleConsole</c>,
/// and a Serilog rolling file under the harness temp log folder) plus
/// global exception handlers on the dispatcher, app domain, and task
/// scheduler. The Plan-02 checklist revealed that the previous
/// <c>AddDebug</c>-only setup silently dropped every runtime error when
/// the harness was launched without a debugger attached.
/// </para>
/// </remarks>
public partial class HarnessApp : Application
{
    private static readonly Action<ILogger, Exception?> s_logStarted = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1, "HarnessStarted"),
        "Harness started. Console output is live."
    );

    private static readonly Action<ILogger, string, Exception?> s_logUnhandled =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, "HarnessUnhandled"),
            "Unhandled exception from {Source}."
        );

    private static readonly Action<ILogger, Exception?> s_logCleanupOnExit = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(3, "HarnessCleanupOnExit"),
        "Harness exiting with Stage still enabled — running DisableAsync to restore desktop."
    );

    private static readonly Action<ILogger, Exception?> s_logCleanupFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(4, "HarnessCleanupFailed"),
        "DisableAsync on harness exit threw — desktop may need manual rescue (./scripts/rescue.ps1)."
    );

    private static readonly Action<ILogger, Exception?> s_logRecoveryFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(5, "HarnessRecoveryFailed"),
        "CrashRecoveryCoordinator on harness startup threw — old snapshot may still need manual rescue."
    );

    private ServiceProvider? _services;
    private ILogger<HarnessApp>? _log;

    /// <summary>The DI container the harness's window resolves services from.</summary>
    internal IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("OnStartup has not run yet.");

    /// <summary>
    /// Allocates a console for the current process so
    /// <c>AddSimpleConsole</c> and <see cref="Console.WriteLine(string)"/>
    /// have somewhere to land when the harness is launched standalone (not
    /// via <c>dotnet run</c> from an existing terminal).
    /// </summary>
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        // Pop a console window so harness users see log output even when
        // launched standalone (double-click, File Explorer, etc.). When
        // invoked via `dotnet run --project tests/App.Harness` from an
        // existing terminal the call still returns true but produces no
        // additional window — the existing shell receives stdout.
        _ = AllocConsole();

        // Truncate the previous-session trace so each launch starts fresh.
        HarnessTrace.Reset();
        HarnessTrace.Write("HarnessApp.OnStartup begin");

        var services = new ServiceCollection();
        ConfigureHarness(services);

        _services = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );

        _log = _services.GetRequiredService<ILogger<HarnessApp>>();

        // Global exception handlers — without these WPF silently swallows
        // exceptions thrown from event handlers, which is exactly how the
        // Plan-02 harness ended up appearing to "do nothing" on click.
        DispatcherUnhandledException += (_, args) =>
        {
            LogUnhandled("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogUnhandled("AppDomain.UnhandledException", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogUnhandled("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // First-chance exceptions: capture EVERY exception thrown anywhere in
        // the process, even before any catch sees it. Routed through the
        // synchronous trace-file helper (Serilog's file sink may buffer
        // across a crash; the trace helper does not). Diagnoses crash paths
        // where Park / Resize / DWM throws and the process dies before
        // logging.
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
            HarnessTrace.Write(
                $"[FirstChance] {args.Exception.GetType().Name}: {args.Exception.Message}"
            );

        // Hard-kill safety net: if the dotnet host gets SIGINT (Ctrl+C in the
        // launching terminal) AppDomain.ProcessExit fires before the WPF
        // OnExit chain. Run the same Stage cleanup so desktop state is
        // restored even on a non-graceful shutdown. Hard-kills via Task
        // Manager bypass this entirely — that's what the snapshot + crash
        // recovery on next start covers.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunStageCleanup();

        // Crash recovery from the previous run (snapshot left on disk because
        // the prior process didn't get to call DisableAsync). Synchronous
        // wait — the harness window only opens after the desktop is sane.
        try
        {
            CrashRecoveryCoordinator coordinator =
                _services.GetRequiredService<CrashRecoveryCoordinator>();
            coordinator.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            s_logRecoveryFailed(_log, ex);
        }

        // Wire up sidebar click + Alt-Tab swap path. Must happen before the
        // first Enable so the SceneClicked subscription is in place when the
        // overlay starts raising events.
        _services.GetRequiredService<StageInteractionCoordinator>().Start();

        s_logStarted(_log, null);
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RunStageCleanup();
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Runs <c>DisableAsync</c> synchronously if Stage is still enabled.
    /// Idempotent and safe to call multiple times — DisableAsync's first
    /// step is "if phase != Enabled return". Used by both the WPF
    /// <see cref="OnExit"/> hook and <c>AppDomain.ProcessExit</c>; whichever
    /// fires first wins, the second sees Disabled and is a no-op.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Cleanup-on-exit must never throw — log and move on; the user can rescue manually."
    )]
    private void RunStageCleanup()
    {
        if (_services is null || _log is null)
        {
            return;
        }
        try
        {
            IStageController stage = _services.GetRequiredService<IStageController>();
            if (!stage.IsEnabled)
            {
                return;
            }
            s_logCleanupOnExit(_log, null);
            // Synchronous wait — we're on the dispatcher exit path with no
            // pumping options. Up to 10 seconds, then give up.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            stage.DisableAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            s_logCleanupFailed(_log, ex);
        }
    }

    private void LogUnhandled(string source, Exception ex)
    {
        // Fallback to Console directly in case the logger itself is what
        // blew up — a silent handler is worse than a loud one here.
        try
        {
            if (_log is not null)
            {
                s_logUnhandled(_log, source, ex);
            }
        }
#pragma warning disable CA1031 // General catch: harness diagnostics must never re-throw.
        catch
        {
            // deliberate swallow
        }
#pragma warning restore CA1031
        Console.Error.WriteLine($"[{source}] {ex}");
    }

    /// <summary>
    /// Registers the minimum service graph the Plan 02 harness needs to
    /// exercise every Wave-1/2 seam. No tray icon, no settings window —
    /// the harness's <see cref="MainWindow"/> drives the scenarios directly.
    /// </summary>
    private static void ConfigureHarness(IServiceCollection services)
    {
        // Logging: fan out to Debug (IDE output), Console (stdout — visible
        // once AllocConsole has run in OnStartup), and Serilog rolling files
        // under the harness log directory.
        var logDir = new HarnessSettingsPathProvider().LogDirectoryPath;
        _ = Directory.CreateDirectory(logDir);
        var logFilePath = Path.Combine(logDir, "harness-.log");

        const string template =
            "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: template,
                formatProvider: CultureInfo.InvariantCulture
            )
            .CreateLogger();

        services.AddLogging(builder =>
        {
            _ = builder.AddFilter(level => level >= LogLevel.Debug);
            _ = builder.AddDebug();
            _ = builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            _ = builder.AddSerilog(serilog, dispose: true);
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
        services.AddSingleton<IBitmapThumbnailFactory, BitmapThumbnailFactory>();
        services.AddSingleton<IThumbnailLayoutEngine, ThumbnailLayoutEngine>();
        services.AddSingleton<WinEventHookThread>();
        services.AddSingleton<IWinEventHookFactory, WinEventHookFactory>();

        // Overlay is resolved via a transient factory so "Show Sidebar" can
        // recreate it after a HideAndRelease (which calls Window.Close and
        // prevents re-show on the same instance).
        services.AddTransient<SidebarOverlay>();
        services.AddSingleton<Func<ISidebarOverlayHandle>>(sp =>
            () => sp.GetRequiredService<SidebarOverlay>()
        );

        // Plan 03: register the full stage controller + collaborators so the
        // harness's "Enable Stage" / "Disable Stage" buttons drive the real
        // algorithm.
        services.AddSingleton<IThumbnailSourceFactory, ThumbnailSourceFactory>();
        services.AddSingleton<MonitorEnumerator>();
        services.AddSingleton<IWorkAreaManager, WorkAreaManager>();
        services.AddSingleton<ISceneGrouper, App.Services.Stage.SceneGrouper>();
        services.AddSingleton<ISceneSwapExecutor, InstantSceneSwapExecutor>();
        services.AddSingleton<IStageOverlayHost, StageOverlayHost>();
        services.AddSingleton<App.Services.Snapshot.ISnapshotPathProvider>(
            _ => new HarnessSnapshotPathProvider()
        );
        services.AddSingleton<App.Services.Snapshot.SnapshotStore>();
        services.AddSingleton<ISnapshotStore>(sp =>
            sp.GetRequiredService<App.Services.Snapshot.SnapshotStore>()
        );
        services.AddSingleton<App.Services.Snapshot.ISnapshotReader>(sp =>
            sp.GetRequiredService<App.Services.Snapshot.SnapshotStore>()
        );
        services.AddSingleton<IStageController, StageController>();

        // Crash recovery: re-attached via App.Shell's coordinator. On harness
        // startup we run RunAsync once to clean up any leftover snapshot
        // from a prior process that died with Stage active.
        services.AddSingleton<CrashRecoveryCoordinator>();

        // Click + Alt-Tab interaction coordinator. Without this the sidebar
        // tile clicks raise SceneClicked but no one is listening — the whole
        // user-driven swap path is dead.
        services.AddSingleton<StageInteractionCoordinator>();
    }

    private sealed class HarnessSnapshotPathProvider : App.Services.Snapshot.ISnapshotPathProvider
    {
        public string SnapshotFilePath { get; } =
            Path.Combine(Path.GetTempPath(), "stagehand-harness-snapshot.json");
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
            Path.Combine(Path.GetTempPath(), "stagehand-harness-settings.json");

        public string LogDirectoryPath { get; } =
            Path.Combine(Path.GetTempPath(), "stagehand-harness-logs");
    }
}
