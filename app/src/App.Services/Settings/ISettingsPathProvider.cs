namespace App.Services.Settings;

/// <summary>
/// Abstraction over filesystem locations for settings and logs — enables tests
/// to redirect to temp directories without touching the real user profile.
/// </summary>
public interface ISettingsPathProvider
{
    /// <summary>Absolute path to the settings JSON file.</summary>
    string SettingsFilePath { get; }

    /// <summary>Absolute path to the log directory.</summary>
    string LogDirectoryPath { get; }
}
