using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace HideAnyWindowManager.Services;

/// <summary>
/// Per-user "start with Windows" support. The service is a normal (asInvoker)
/// executable, so a HKCU Run entry is enough — no Task Scheduler, no elevation,
/// and no UAC prompt at logon. The installer writes the same value.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutostartManager
{
    public const string RunValueName = "HideAnyAppsService";
    public const string StartServiceArg = "--start-service";
    public const string StopServiceArg = "--stop-service";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>True if the per-user Run entry exists.</summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string value && value.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>Creates/overwrites the HKCU Run entry.</summary>
    public bool TryEnable()
    {
        try
        {
            var command = BuildCommand();
            if (command == null) return false;

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null) return false;
            key.SetValue(RunValueName, command, RegistryValueKind.String);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Removes the Run entry. Idempotent.</summary>
    public bool TryDisable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Prefer the service exe sitting next to the manager. The manager
    /// --start-service fallback keeps development builds (where the service
    /// lives under tooling/ is not next to the exe) working.
    /// </summary>
    private static string? BuildCommand()
    {
        var serviceExe = Path.Combine(AppContext.BaseDirectory, "HideAnyWindowService.exe");
        if (File.Exists(serviceExe))
            return $"\"{serviceExe}\"";

        var managerExe = ManagerExePath();
        if (string.IsNullOrEmpty(managerExe))
            return null;
        return $"\"{managerExe}\" {StartServiceArg}";
    }

    private static string ManagerExePath()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
        catch { return ""; }
    }
}
