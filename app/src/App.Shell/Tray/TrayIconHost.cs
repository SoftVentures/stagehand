using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using App.Core.Branding;
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

    private static readonly Action<ILogger, Exception?> LogStageTogglePlaceholder =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(4004, nameof(LogStageTogglePlaceholder)),
            "Stage toggle requested — wired in Plan 03."
        );

    private readonly ILogger<TrayIconHost> _log;
    private readonly ISettingsPathProvider _paths;
    private readonly Func<SettingsWindow> _settingsFactory;
    private TaskbarIcon? _icon;
    private SettingsWindow? _settingsWindow;

    /// <summary>Creates the host. The icon is not realised until <see cref="Start"/> is called.</summary>
    public TrayIconHost(
        ILogger<TrayIconHost> log,
        ISettingsPathProvider paths,
        Func<SettingsWindow> settingsFactory
    )
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settingsFactory);
        _log = log;
        _paths = paths;
        _settingsFactory = settingsFactory;
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

    private void OnLeftClick() => LogStageTogglePlaceholder(_log, null);
}

/// <summary>Minimal <see cref="ICommand"/> adapter for non-MVVM tray callbacks.</summary>
internal sealed class DelegateCommand : ICommand
{
    private readonly Action _action;

    public DelegateCommand(Action action) => _action = action;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _action();

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
