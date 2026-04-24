using Microsoft.Extensions.Logging;

namespace App.Services.Updates;

/// <summary>
/// Placeholder <see cref="IUpdateService"/> for Plan 01. Logs a single warning
/// on first use; real update flow lands in Plan 05.
/// </summary>
public sealed class StubUpdateService : IUpdateService
{
    private static readonly Action<ILogger, Exception?> LogStubWarning = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(3001, nameof(LogStubWarning)),
        "UpdateService is a stub — wired in Plan 05."
    );

    private readonly ILogger<StubUpdateService> _log;
    private int _warned;

    /// <summary>Creates a new stub update service.</summary>
    public StubUpdateService(ILogger<StubUpdateService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public Task<bool> CheckForUpdatesAsync(CancellationToken ct)
    {
        WarnOnce();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task ApplyPendingUpdateAsync(CancellationToken ct)
    {
        WarnOnce();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            LogStubWarning(_log, null);
        }
    }
}
