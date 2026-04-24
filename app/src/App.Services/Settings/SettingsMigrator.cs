using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace App.Services.Settings;

/// <summary>
/// Applies schema migrations to a raw settings JSON document. Plan 01 ships
/// schema v0 — no migration needed. Plan 04 will add v0→v1 when sub-records
/// gain properties.
/// </summary>
public sealed class SettingsMigrator
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Migrates <paramref name="raw"/> forward from <paramref name="fromVersion"/>
    /// to <see cref="AppSettings.CurrentSchemaVersion"/> and returns the
    /// deserialised <see cref="AppSettings"/>.
    /// </summary>
    /// <remarks>
    /// v0 is the baseline: deserialise the document as-is. Plan 04 adds the
    /// v0→v1 migrator — until then <paramref name="fromVersion"/> is verified
    /// and otherwise ignored.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member kept for forward compatibility — Plan 04 migrations will hold state."
    )]
    public AppSettings Migrate(JsonDocument raw, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(raw);
        _ = fromVersion;

        return raw.Deserialize<AppSettings>(DeserializeOptions) ?? AppSettings.Defaults();
    }
}
