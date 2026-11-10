using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace HideAnyWindowManager.Util;

/// <summary>
/// Keyboard-state helpers for the two hotkey capture surfaces (the standalone
/// capture dialog and the inline capture in Settings). This file touches WinUI
/// types, so the test project excludes it.
/// </summary>
public static class HotkeyCaptureKeys
{
    public static bool IsModifier(VirtualKey key)
        => key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>Keys that should keep their normal dialog behavior when no modifier is held.</summary>
    public static bool IsUiKey(VirtualKey key)
        => key is VirtualKey.Escape or VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Space
            or VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right;

    public static bool IsDown(VirtualKey key)
        => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down)
           == CoreVirtualKeyStates.Down;

    public static bool AnyModifierDown()
        => IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)
            || IsDown(VirtualKey.Shift) || IsDown(VirtualKey.LeftWindows)
            || IsDown(VirtualKey.RightWindows);

    /// <summary>
    /// Builds a canonical chord from <paramref name="key"/> plus the modifiers
    /// currently held. Returns false for unsupported keys or when no modifier
    /// is down.
    /// </summary>
    public static bool TryBuild(VirtualKey key, out string candidate)
    {
        candidate = "";
        string? keyName = KeyName(key);
        if (keyName == null)
            return false;

        bool ctrl = IsDown(VirtualKey.Control);
        bool alt = IsDown(VirtualKey.Menu);
        bool shift = IsDown(VirtualKey.Shift);
        bool win = IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);
        if (!(ctrl || alt || shift || win))
            return false;

        return HotkeyText.TryNormalize(
            HotkeyText.Compose(ctrl, alt, shift, win, keyName), out candidate);
    }

    public static string? KeyName(VirtualKey key)
    {
        int code = (int)key;

        if (key >= VirtualKey.A && key <= VirtualKey.Z)
            return ((char)('A' + (code - (int)VirtualKey.A))).ToString();

        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            return ((char)('0' + (code - (int)VirtualKey.Number0))).ToString();

        if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
            return "Numpad" + (code - (int)VirtualKey.NumberPad0);

        if (key >= VirtualKey.F1 && key <= VirtualKey.F24)
            return "F" + (code - (int)VirtualKey.F1 + 1);

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Escape",
            VirtualKey.Tab => "Tab",
            VirtualKey.Back => "Backspace",
            VirtualKey.Delete => "Delete",
            VirtualKey.Insert => "Insert",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            _ => null,
        };
    }
}
