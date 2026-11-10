using System.Text.Json.Serialization;

namespace HideAnyWindowManager.Models;

public sealed class Rule
{
    public string Id { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>True if the service should hide this app. The AHK service writes
    /// 1/0 instead of true/false, so accept both.</summary>
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional per-app toggle chord (e.g. "Ctrl+Alt+1"). Empty = no hotkey.</summary>
    public string Hotkey { get; set; } = "";

    /// <summary>Generates a stable id from an exe name, e.g. "magnify.exe" -&gt; "magnify-exe".</summary>
    public static string IdFromExe(string exe)
        => exe.ToLowerInvariant().Replace('.', '-');
}
