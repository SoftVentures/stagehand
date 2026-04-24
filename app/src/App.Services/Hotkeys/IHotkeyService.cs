namespace App.Services.Hotkeys;

/// <summary>
/// Registers and unregisters global hotkey chords. Real implementation wired in Plan 04.
/// </summary>
public interface IHotkeyService
{
    /// <summary>Registers a chord (e.g. <c>"Ctrl+Alt+S"</c>) under <paramref name="id"/>.</summary>
    void Register(string id, string chord, Action onPressed);

    /// <summary>Unregisters a previously-registered chord by <paramref name="id"/>.</summary>
    void Unregister(string id);
}
