using System.Collections.Generic;

namespace HideAnyWindowManager.Models;

public sealed class Config
{
    public int SchemaVersion { get; set; } = 2;
    public string ServiceState { get; set; } = "running";
    public HotkeySettings Hotkeys { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
}
