using System;
using System.Collections.Generic;
using System.Linq;

namespace HideAnyWindowManager.Util;

/// <summary>
/// Parse/format the hotkey strings shared with the AHK service
/// (<c>Ctrl+Alt+Shift+H</c>). Deliberately free of WinUI so the unit-test
/// assembly can link this file directly.
/// </summary>
public static class HotkeyText
{
    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "Space",
        ["enter"] = "Enter",
        ["return"] = "Enter",
        ["escape"] = "Escape",
        ["esc"] = "Escape",
        ["tab"] = "Tab",
        ["backspace"] = "Backspace",
        ["delete"] = "Delete",
        ["del"] = "Delete",
        ["insert"] = "Insert",
        ["ins"] = "Insert",
        ["home"] = "Home",
        ["end"] = "End",
        ["pageup"] = "PageUp",
        ["pagedown"] = "PageDown",
        ["up"] = "Up",
        ["down"] = "Down",
        ["left"] = "Left",
        ["right"] = "Right",
        ["printscreen"] = "PrintScreen",
        ["pause"] = "Pause",
        ["scrolllock"] = "ScrollLock",
        ["capslock"] = "CapsLock",
        ["numlock"] = "NumLock",
    };

    /// <summary>Builds the canonical form from modifiers + one key name.</summary>
    public static string Compose(bool ctrl, bool alt, bool shift, bool win, string key)
    {
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(key);
        return string.Join("+", parts);
    }

    /// <summary>
    /// Canonicalizes <paramref name="raw"/> to <c>Ctrl+Alt+Shift+Win+Key</c>
    /// order. Returns false when the string is empty, has more than one
    /// non-modifier key, has no modifier, or uses an unsupported key name.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;

        foreach (var part in raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    continue;
                case "alt":
                    alt = true;
                    continue;
                case "shift":
                    shift = true;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    win = true;
                    continue;
            }

            if (key != null)
                return false;               // more than one non-modifier key
            if (!TryCanonicalKey(part, out var canonical))
                return false;
            key = canonical;
        }

        if (key == null || !(ctrl || alt || shift || win))
            return false;

        normalized = Compose(ctrl, alt, shift, win, key);
        return true;
    }

    public static string Normalize(string? raw)
        => TryNormalize(raw, out var normalized) ? normalized : "";

    public static bool IsValid(string? raw) => TryNormalize(raw, out _);

    /// <summary>True when both are non-empty and represent the same chord.</summary>
    public static bool Conflicts(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        return na.Length > 0 && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>First unused <c>Ctrl+Alt+1..9</c>, or "" when all nine are taken.</summary>
    public static string NextFreeSlot(IEnumerable<string?> used)
    {
        var taken = new HashSet<string>(
            used.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => Normalize(u)),
            StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i <= 9; i++)
        {
            var candidate = $"Ctrl+Alt+{i}";
            if (!taken.Contains(candidate))
                return candidate;
        }
        return "";
    }

    private static bool TryCanonicalKey(string token, out string canonical)
    {
        canonical = "";

        // A-Z
        if (token.Length == 1 && char.IsAsciiLetter(token[0]))
        {
            canonical = char.ToUpperInvariant(token[0]).ToString();
            return true;
        }

        // 0-9
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            canonical = token;
            return true;
        }

        // F1-F24
        if (token.Length is >= 2 and <= 3
            && (token[0] == 'F' || token[0] == 'f')
            && int.TryParse(token.AsSpan(1), out int fn)
            && fn is >= 1 and <= 24)
        {
            canonical = "F" + fn;
            return true;
        }

        // Numpad0-Numpad9
        if (token.Length == 7
            && token.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
            && char.IsAsciiDigit(token[6]))
        {
            canonical = "Numpad" + token[6];
            return true;
        }

        if (NamedKeys.TryGetValue(token, out var named))
        {
            canonical = named;
            return true;
        }

        return false;
    }
}
