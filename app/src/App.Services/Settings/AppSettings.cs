namespace App.Services.Settings;

/// <summary>
/// Immutable root settings record. Sub-records are populated in Plan 04;
/// Plan 01 ships empty shells so the schema + migrator pipeline exists.
/// </summary>
public sealed record AppSettings(
    int SchemaVersion,
    GeneralSettings General,
    AppearanceSettings Appearance,
    BehaviorSettings Behavior,
    UpdatesSettings Updates,
    LoggingSettings Logging
)
{
    /// <summary>Current schema version understood by this build.</summary>
    public const int CurrentSchemaVersion = 0;

    /// <summary>Returns a fresh <see cref="AppSettings"/> populated with defaults.</summary>
    public static AppSettings Defaults() =>
        new(
            SchemaVersion: CurrentSchemaVersion,
            General: new GeneralSettings(),
            Appearance: new AppearanceSettings(),
            Behavior: new BehaviorSettings(),
            Updates: new UpdatesSettings(),
            Logging: new LoggingSettings()
        );
}

/// <summary>General application settings. Populated in Plan 04.</summary>
public sealed record GeneralSettings;

/// <summary>Appearance-related settings. Populated in Plan 04.</summary>
public sealed record AppearanceSettings;

/// <summary>Behavior-related settings. Populated in Plan 04.</summary>
public sealed record BehaviorSettings;

/// <summary>Auto-update settings. Populated in Plan 05.</summary>
public sealed record UpdatesSettings;

/// <summary>Logging overrides (per-source minimum level). Populated in Plan 04.</summary>
public sealed record LoggingSettings(
    System.Collections.Immutable.ImmutableDictionary<string, string>? Levels = null
)
{
    /// <summary>Parameterless ctor for record deserialisation with defaults.</summary>
    public LoggingSettings()
        : this(Levels: null) { }
}
