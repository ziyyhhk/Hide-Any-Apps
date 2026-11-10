namespace HideAnyWindowManager.Models;

/// <summary>Global hotkeys. Defaults intentionally match service/config.ahk.</summary>
public sealed class HotkeySettings
{
    /// <summary>Restores every hidden window and pauses auto-hiding; press again to resume.</summary>
    public string SuspendAll { get; set; } = "Ctrl+Alt+H";

    /// <summary>Hides whatever window currently has focus.</summary>
    public string HideForeground { get; set; } = "Ctrl+Alt+Shift+H";

    /// <summary>Restores the most recently hidden window.</summary>
    public string UnhideLast { get; set; } = "Ctrl+Alt+Shift+U";
}
