using Microsoft.Extensions.Logging;

namespace App.Services.Hotkeys;

/// <summary>
/// Placeholder <see cref="IHotkeyService"/> for Plan 01. Logs a single warning on
/// first use; full Win32 registration lands in Plan 04.
/// </summary>
public sealed class StubHotkeyService : IHotkeyService
{
    private static readonly Action<ILogger, Exception?> LogStubWarning = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2001, nameof(LogStubWarning)),
        "HotkeyService is a stub — wired in Plan 04."
    );

    private readonly ILogger<StubHotkeyService> _log;
    private int _warned;

    /// <summary>Creates a new stub hotkey service.</summary>
    public StubHotkeyService(ILogger<StubHotkeyService> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public void Register(string id, string chord, Action onPressed) => WarnOnce();

    /// <inheritdoc />
    public void Unregister(string id) => WarnOnce();

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            LogStubWarning(_log, null);
        }
    }
}
