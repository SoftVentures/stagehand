using System.IO;
using System.Text.Json;
using App.Core.Time;
using Microsoft.Extensions.Logging;

namespace App.Services.Settings;

/// <summary>
/// Default <see cref="ISettingsService"/> implementation. Reads on construction,
/// writes atomically via temp-file + <see cref="File.Replace(string, string, string?)"/>,
/// and raises <see cref="Changed"/> after successful saves.
/// </summary>
/// <remarks>
/// If the on-disk schema version exceeds <see cref="AppSettings.CurrentSchemaVersion"/>,
/// the service loads defaults and logs <see cref="LogLevel.Critical"/>. A future
/// <c>IsReadOnly</c> flag will surface this to the UI (Plan 04).
/// </remarks>
public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsPathProvider _paths;
    private readonly ILogger<SettingsService> _log;
    private readonly SettingsMigrator _migrator;
    private readonly IClock _clock;
    private readonly object _writeLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Action<ILogger, string, Exception?> LogSaveFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1001, nameof(LogSaveFailed)),
            "Failed to save settings to {Path}."
        );

    private static readonly Action<ILogger, string, Exception?> LogAccessDenied =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1002, nameof(LogAccessDenied)),
            "Access denied writing settings to {Path}."
        );

    private static readonly Action<ILogger, string, Exception?> LogSeedFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1003, nameof(LogSeedFailed)),
            "Failed to seed default settings at {Path}."
        );

    private static readonly Action<ILogger, int, int, Exception?> LogSchemaTooHigh =
        LoggerMessage.Define<int, int>(
            LogLevel.Critical,
            new EventId(1004, nameof(LogSchemaTooHigh)),
            "Settings schema v{Version} is newer than supported v{Current}. Loading defaults read-only."
        );

    private static readonly Action<ILogger, Exception?> LogLoadFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1005, nameof(LogLoadFailed)),
        "Failed to load settings; falling back to defaults."
    );

    /// <summary>
    /// Creates a new <see cref="SettingsService"/>. The ctor loads the settings
    /// file synchronously (or writes defaults if missing).
    /// </summary>
    public SettingsService(
        ISettingsPathProvider paths,
        ILogger<SettingsService> log,
        SettingsMigrator migrator,
        IClock clock
    )
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(migrator);
        ArgumentNullException.ThrowIfNull(clock);

        _paths = paths;
        _log = log;
        _migrator = migrator;
        _clock = clock;
        Current = LoadOrDefaults();
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<AppSettings>? Changed;

    /// <inheritdoc />
    public Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        try
        {
            lock (_writeLock)
            {
                WriteSync(settings);
                Current = settings;
            }
        }
        catch (IOException ex)
        {
            LogSaveFailed(_log, _paths.SettingsFilePath, ex);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogAccessDenied(_log, _paths.SettingsFilePath, ex);
            throw;
        }

        // Clock reserved for Plan 04 UpdatedAt timestamps.
        _ = _clock;

        Changed?.Invoke(this, settings);
        return Task.CompletedTask;
    }

    private AppSettings LoadOrDefaults()
    {
        if (!File.Exists(_paths.SettingsFilePath))
        {
            var defaults = AppSettings.Defaults();
            try
            {
                WriteSync(defaults);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Rethrow: App.OnStartup deliberately resolves SettingsService up
                // front so I/O failures surface before the tray starts. Silently
                // continuing with in-memory defaults would hide a broken install
                // and leave SaveAsync failing on every user change.
                LogSeedFailed(_log, _paths.SettingsFilePath, ex);
                throw;
            }
            return defaults;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_paths.SettingsFilePath));
            var fromVersion = doc.RootElement.TryGetProperty("schemaVersion", out JsonElement v)
                ? v.GetInt32()
                : AppSettings.CurrentSchemaVersion;

            if (fromVersion > AppSettings.CurrentSchemaVersion)
            {
                LogSchemaTooHigh(_log, fromVersion, AppSettings.CurrentSchemaVersion, null);
                return AppSettings.Defaults();
            }

            return _migrator.Migrate(doc, fromVersion) ?? AppSettings.Defaults();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            LogLoadFailed(_log, ex);
            return AppSettings.Defaults();
        }
    }

    private void WriteSync(AppSettings settings)
    {
        var tmp = _paths.SettingsFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        if (File.Exists(_paths.SettingsFilePath))
        {
            File.Replace(tmp, _paths.SettingsFilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tmp, _paths.SettingsFilePath);
        }
    }
}
