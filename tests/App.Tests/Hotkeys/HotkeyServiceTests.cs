using System.Diagnostics.CodeAnalysis;
using App.Interop;
using App.Services.Hotkeys;
using FluentAssertions;
using Xunit;

namespace App.Tests.Hotkeys;

[SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit convention: underscore-separated test method names describe the scenario."
)]
public sealed class HotkeyServiceTests
{
    [Fact]
    public void TryParseChord_Ctrl_Alt_S()
    {
        var ok = HotkeyService.TryParseChord("Ctrl+Alt+S", out var mods, out var vk);
        ok.Should().BeTrue();
        mods.Should().Be(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT);
        vk.Should().Be(NativeMethods.VK_S);
    }

    [Fact]
    public void TryParseChord_Is_Case_Insensitive()
    {
        HotkeyService.TryParseChord("ctrl+alt+s", out var mods, out var vk).Should().BeTrue();
        mods.Should().Be(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT);
        vk.Should().Be(NativeMethods.VK_S);
    }

    [Fact]
    public void TryParseChord_Win_Plus_F1()
    {
        var ok = HotkeyService.TryParseChord("Win+F1", out var mods, out var vk);
        ok.Should().BeTrue();
        mods.Should().Be(NativeMethods.MOD_WIN);
        vk.Should().Be(0x70u); // VK_F1
    }

    [Fact]
    public void TryParseChord_Shift_Plus_F12()
    {
        var ok = HotkeyService.TryParseChord("Shift+F12", out var mods, out var vk);
        ok.Should().BeTrue();
        mods.Should().Be(NativeMethods.MOD_SHIFT);
        vk.Should().Be(0x7Bu); // VK_F12
    }

    [Fact]
    public void TryParseChord_Single_Letter()
    {
        var ok = HotkeyService.TryParseChord("A", out var mods, out var vk);
        ok.Should().BeTrue();
        mods.Should().Be(0u);
        vk.Should().Be(0x41u); // 'A'
    }

    [Fact]
    public void TryParseChord_Digit()
    {
        var ok = HotkeyService.TryParseChord("Ctrl+5", out var mods, out var vk);
        ok.Should().BeTrue();
        mods.Should().Be(NativeMethods.MOD_CONTROL);
        vk.Should().Be(0x35u); // '5'
    }

    [Fact]
    public void TryParseChord_Empty_Returns_False()
    {
        HotkeyService.TryParseChord(string.Empty, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseChord_Unknown_Modifier_Returns_False()
    {
        HotkeyService.TryParseChord("Wibble+S", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseChord_Unknown_Key_Returns_False()
    {
        HotkeyService.TryParseChord("Ctrl+!", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseChord_F25_Returns_False()
    {
        HotkeyService.TryParseChord("F25", out _, out _).Should().BeFalse();
    }
}
