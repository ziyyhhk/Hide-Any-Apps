using HideAnyWindowManager.Util;
using Xunit;

public class HotkeyTextTests
{
    [Theory]
    [InlineData("shift+ctrl+alt+h", "Ctrl+Alt+Shift+H")]
    [InlineData("Alt+Ctrl+1", "Ctrl+Alt+1")]
    [InlineData("ctrl+alt+shift+win+f12", "Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Control+F5", "Ctrl+F5")]
    [InlineData("Win+space", "Win+Space")]
    [InlineData("alt+numpad4", "Alt+Numpad4")]
    public void TryNormalize_CanonicalizesModifierOrderAndKeyCase(string raw, string expected)
    {
        Assert.True(HotkeyText.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("H")]                 // no modifier: would swallow typing
    [InlineData("Ctrl")]              // modifier only
    [InlineData("Ctrl+H+J")]          // two non-modifier keys
    [InlineData("Ctrl+F25")]          // out of range
    [InlineData("Ctrl+Banana")]       // unknown key name
    [InlineData("Ctrl++")]            // nothing after the separator
    public void TryNormalize_RejectsInvalidChords(string? raw)
    {
        Assert.False(HotkeyText.TryNormalize(raw, out _));
        Assert.Equal("", HotkeyText.Normalize(raw));
        Assert.False(HotkeyText.IsValid(raw));
    }

    [Fact]
    public void Compose_EmitsOnlyPressedModifiersInCanonicalOrder()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+Q",
            HotkeyText.Compose(ctrl: true, alt: true, shift: true, win: true, key: "Q"));
        Assert.Equal("Shift+Tab",
            HotkeyText.Compose(ctrl: false, alt: false, shift: true, win: false, key: "Tab"));
    }

    [Fact]
    public void Conflicts_ComparesNormalizedChords()
    {
        Assert.True(HotkeyText.Conflicts("ctrl+alt+1", "Alt+Ctrl+1"));
        Assert.False(HotkeyText.Conflicts("Ctrl+Alt+1", "Ctrl+Alt+2"));
        Assert.False(HotkeyText.Conflicts("", ""));
        Assert.False(HotkeyText.Conflicts(null, "Ctrl+Alt+1"));
    }

    [Fact]
    public void NextFreeSlot_WalksPastUsedSlots()
    {
        Assert.Equal("Ctrl+Alt+1", HotkeyText.NextFreeSlot([]));
        Assert.Equal("Ctrl+Alt+2",
            HotkeyText.NextFreeSlot(["Ctrl+Alt+1", null, ""]));
        Assert.Equal("Ctrl+Alt+3",
            HotkeyText.NextFreeSlot(["ctrl+alt+1", "Ctrl+Alt+2"]));
    }

    [Fact]
    public void NextFreeSlot_ReturnsEmptyWhenAllNineAreTaken()
    {
        var all = Enumerable.Range(1, 9).Select(i => $"Ctrl+Alt+{i}").ToArray();
        Assert.Equal("", HotkeyText.NextFreeSlot(all));
    }
}
