namespace App.Services.Updates;

/// <summary>
/// Checks for and applies application updates. Real implementation wired in Plan 05.
/// </summary>
public interface IUpdateService
{
    /// <summary>Returns <see langword="true"/> if an update is available.</summary>
    Task<bool> CheckForUpdatesAsync(CancellationToken ct);

    /// <summary>Applies a previously-downloaded pending update.</summary>
    Task ApplyPendingUpdateAsync(CancellationToken ct);
}
