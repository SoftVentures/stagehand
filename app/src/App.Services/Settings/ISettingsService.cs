using System.Diagnostics.CodeAnalysis;

namespace App.Services.Settings;

/// <summary>
/// Provides read/write access to the immutable <see cref="AppSettings"/>
/// snapshot. Consumers subscribe to <see cref="Changed"/> to react to updates.
/// </summary>
public interface ISettingsService
{
    /// <summary>Current in-memory settings snapshot.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after a successful <see cref="SaveAsync"/>.</summary>
    [SuppressMessage(
        "Design",
        "CA1003:Use generic event handler instances",
        Justification = "AppSettings is intentionally passed as the event payload; it is an immutable record, not an EventArgs subclass."
    )]
    event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Persists <paramref name="settings"/> atomically and updates <see cref="Current"/>.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
