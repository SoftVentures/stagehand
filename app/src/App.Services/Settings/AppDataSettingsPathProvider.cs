using System.IO;
using App.Core.Branding;

namespace App.Services.Settings;

/// <summary>
/// Default <see cref="ISettingsPathProvider"/> that routes settings to
/// <c>%APPDATA%\{BrandConstants.ProductFolderName}</c> (roaming) and logs to
/// <c>%LOCALAPPDATA%\{BrandConstants.ProductFolderName}\logs</c> (non-roaming).
/// </summary>
public sealed class AppDataSettingsPathProvider : ISettingsPathProvider
{
    /// <summary>Initialises paths and ensures their parent directories exist.</summary>
    public AppDataSettingsPathProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );

        var settingsFolder = Path.Combine(appData, BrandConstants.ProductFolderName);
        Directory.CreateDirectory(settingsFolder);
        SettingsFilePath = Path.Combine(settingsFolder, "settings.json");

        LogDirectoryPath = Path.Combine(localAppData, BrandConstants.ProductFolderName, "logs");
        Directory.CreateDirectory(LogDirectoryPath);
    }

    /// <inheritdoc />
    public string SettingsFilePath { get; }

    /// <inheritdoc />
    public string LogDirectoryPath { get; }
}
