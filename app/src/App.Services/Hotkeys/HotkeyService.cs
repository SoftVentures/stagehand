using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using App.Core.Branding;
using App.Interop;
using App.Interop.Threading;
using Microsoft.Extensions.Logging;

namespace App.Services.Hotkeys;

/// <summary>
/// Production <see cref="IHotkeyService"/>. Owns a hidden message-only
/// window to receive <c>WM_HOTKEY</c> notifications and dispatches each
/// match to its registered callback via <see cref="UiDispatcher"/>.
/// Plan 03 §S8 / §Design.11.
/// </summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private static readonly Action<ILogger, string, Exception?> s_logRegisterFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(15001, nameof(HotkeyService) + ".RegisterFailed"),
            "HotkeyService: failed to register chord '{Chord}'. Win32 RegisterHotKey returned false."
        );

    private static readonly Action<ILogger, string, Exception?> s_logRegistered =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(15002, nameof(HotkeyService) + ".Registered"),
            "HotkeyService: chord '{Chord}' registered."
        );

    private readonly UiDispatcher _ui;
    private readonly ILogger<HotkeyService> _log;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed in Dispose() guarded by _disposed flag."
    )]
    private readonly MessageOnlyWindow _window;
    private readonly Dictionary<string, Registration> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Registration> _byHotkeyId = new();
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>Production constructor — must be called on the UI thread.</summary>
    public HotkeyService(UiDispatcher ui, ILogger<HotkeyService> log)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _ui.AssertOnUiThread();
        _window = new MessageOnlyWindow(BrandConstants.HotkeyWindowClassPrefix);
        _window.MessageReceived += OnMessage;
    }

    /// <inheritdoc />
    public void Register(string id, string chord, Action onPressed)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(chord);
        ArgumentNullException.ThrowIfNull(onPressed);

        if (_byId.ContainsKey(id))
        {
            Unregister(id);
        }

        if (!TryParseChord(chord, out uint mods, out uint vk))
        {
            s_logRegisterFailed(_log, chord, null);
            return;
        }

        var hotkeyId = Interlocked.Increment(ref _nextId);
        if (
            !NativeMethods.RegisterHotKey(
                _window.Hwnd,
                hotkeyId,
                mods | NativeMethods.MOD_NOREPEAT,
                vk
            )
        )
        {
            var err = Marshal.GetLastWin32Error();
            s_logRegisterFailed(_log, chord, new System.ComponentModel.Win32Exception(err));
            return;
        }

        var registration = new Registration(id, hotkeyId, chord, onPressed);
        _byId[id] = registration;
        _byHotkeyId[hotkeyId] = registration;
        s_logRegistered(_log, chord, null);
    }

    /// <inheritdoc />
    public void Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!_byId.TryGetValue(id, out Registration? r))
        {
            return;
        }
        _byId.Remove(id);
        _byHotkeyId.Remove(r.HotkeyId);
        _ = NativeMethods.UnregisterHotKey(_window.Hwnd, r.HotkeyId);
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA1816:Dispose methods should call SuppressFinalize",
        Justification = "Sealed class with no finalizer."
    )]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (Registration r in _byHotkeyId.Values)
        {
            _ = NativeMethods.UnregisterHotKey(_window.Hwnd, r.HotkeyId);
        }
        _byHotkeyId.Clear();
        _byId.Clear();
        _window.MessageReceived -= OnMessage;
        _window.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Subscriber callback errors must not crash the message pump."
    )]
    private void OnMessage(object? sender, MessageOnlyWindowMessageEventArgs e)
    {
        if (e.Message != NativeMethods.WM_HOTKEY)
        {
            return;
        }
        var hotkeyId = e.WParam.ToInt32();
        if (!_byHotkeyId.TryGetValue(hotkeyId, out Registration? r))
        {
            return;
        }
        try
        {
            r.OnPressed();
        }
        catch
        {
            // intentional: hotkey-callback isolation
        }
    }

    /// <summary>
    /// Parses a chord like <c>"Ctrl+Alt+S"</c> into (modifiers, virtual key
    /// code). Case-insensitive. Returns <see langword="false"/> when the
    /// chord cannot be parsed.
    /// </summary>
    public static bool TryParseChord(string chord, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(chord))
        {
            return false;
        }
        string[] parts = chord.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "WIN":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    return false;
            }
        }
        // Last token is the key.
        var key = parts[^1].ToUpperInvariant();
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z')
            {
                vk = (uint)c;
                return true;
            }
            if (c is >= '0' and <= '9')
            {
                vk = (uint)c;
                return true;
            }
        }
        // Function keys F1..F24.
        if (
            key.StartsWith('F')
            && int.TryParse(
                key.AsSpan(1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var fnum
            )
        )
        {
            if (fnum is >= 1 and <= 24)
            {
                vk = (uint)(0x70 + (fnum - 1)); // VK_F1 = 0x70
                return true;
            }
        }
        return false;
    }

    private sealed record Registration(string Id, int HotkeyId, string Chord, Action OnPressed);
}
