namespace HideAnyWindowManager.Models;

public sealed class Rule
{
    public string Id { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Optional per-app toggle chord (e.g. "Ctrl+Alt+1"). Empty = no hotkey.</summary>
    public string Hotkey { get; set; } = "";

    /// <summary>Generates a stable id from an exe name, e.g. "magnify.exe" -&gt; "magnify-exe".</summary>
    public static string IdFromExe(string exe)
        => exe.ToLowerInvariant().Replace('.', '-');
}
