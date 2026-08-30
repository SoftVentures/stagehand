using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using App.Core.Branding;
using App.Core.Stage;
using App.Services.Settings;
using App.Shell.Settings;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;

namespace App.Shell.Tray;

/// <summary>
/// Owns the system tray icon and its context menu. Created by the DI container
/// and started from <see cref="ShellApp.OnStartup"/>. Exit is routed through
/// <see cref="Application.Shutdown()"/> since the app uses
/// <c>ShutdownMode=OnExplicitShutdown</c>.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private static readonly Action<ILogger, Exception?> LogTrayStarted = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(4001, nameof(LogTrayStarted)),
        "Tray icon started."
    );

    private static readonly Action<ILogger, string, Exception?> LogOpenDiagnosticsFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4002, nameof(LogOpenDiagnosticsFailed)),
            "Failed to open log folder {Path}."
        );

    private static readonly Action<ILogger, Exception?> LogExitRequested = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(4003, nameof(LogExitRequested)),
        "Exit requested from tray."
    );

    private static readonly Action<ILogger, Exception?> LogToggleEnable = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(4004, nameof(LogToggleEnable)),
        "Tray left-click: enabling Stage."
    );

    private static readonly Action<ILogger, Exception?> LogToggleDisable = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(4005, nameof(LogToggleDisable)),
        "Tray left-click: disabling Stage."
    );

    private static readonly Action<ILogger, Exception?> LogToggleFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(4006, nameof(LogToggleFailed)),
        "Tray left-click: stage toggle failed."
    );

    private readonly ILogger<TrayIconHost> _log;
    private readonly ISettingsPathProvider _paths;
    private readonly Func<SettingsWindow> _settingsFactory;
    private readonly IStageController _stage;
    private TaskbarIcon? _icon;
    private SettingsWindow? _settingsWindow;

    /// <summary>Creates the host. The icon is not realised until <see cref="Start"/> is called.</summary>
    public TrayIconHost(
        ILogger<TrayIconHost> log,
        ISettingsPathProvider paths,
        Func<SettingsWindow> settingsFactory,
        IStageController stage
    )
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settingsFactory);
        ArgumentNullException.ThrowIfNull(stage);
        _log = log;
        _paths = paths;
        _settingsFactory = settingsFactory;
        _stage = stage;
    }

    /// <summary>Creates the <see cref="TaskbarIcon"/> and wires its context menu.</summary>
    public void Start()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = BrandConstants.TrayTooltip,
            IconSource = new BitmapImage(
                new Uri("pack://application:,,,/Assets/TrayIcon.ico", UriKind.Absolute)
            ),
        };

        var menu = new ContextMenu();
        menu.Items.Add(BuildItem("Settings…", OpenSettings));
        menu.Items.Add(BuildItem("Open Diagnostics", OpenDiagnostics));
        menu.Items.Add(BuildItem("About", ShowAbout));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildItem("Exit", Exit));
        icon.ContextMenu = menu;

        icon.LeftClickCommand = new DelegateCommand(OnLeftClick);

        _icon = icon;
        LogTrayStarted(_log, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private static MenuItem BuildItem(string header, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = _settingsFactory();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OpenDiagnostics()
    {
        try
        {
            // Use explorer.exe explicitly — relying on shell-execute for a directory
            // path makes behaviour depend on whichever handler is registered for "."
            // verbs, which differs across OS configurations.
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_paths.LogDirectoryPath}\"",
                    UseShellExecute = false,
                }
            );
        }
        catch (Exception ex)
        {
            LogOpenDiagnosticsFailed(_log, _paths.LogDirectoryPath, ex);
        }
    }

    private static void ShowAbout()
    {
        var version = typeof(TrayIconHost).Assembly.GetName().Version?.ToString() ?? "dev";
        MessageBox.Show(
            $"{BrandConstants.ProductName}\nVersion: {version}\n\n{BrandConstants.AboutDescription}",
            BrandConstants.AboutTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private void Exit()
    {
        LogExitRequested(_log, null);
        Application.Current.Shutdown();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Tray click is an entry point — every failure must be caught and surfaced rather than crashing the dispatcher."
    )]
    private async void OnLeftClick()
    {
        try
        {
            if (_stage.IsEnabled)
            {
                LogToggleDisable(_log, null);
                await _stage.DisableAsync(CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                LogToggleEnable(_log, null);
                await _stage.EnableAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            LogToggleFailed(_log, ex);
        }
    }
}

/// <summary>Minimal <see cref="ICommand"/> adapter for non-MVVM tray callbacks.</summary>
internal sealed class DelegateCommand(Action action) : ICommand
{
    private readonly Action _action = action;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _action();

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
