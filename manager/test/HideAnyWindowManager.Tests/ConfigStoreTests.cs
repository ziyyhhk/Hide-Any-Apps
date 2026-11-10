using System.IO;
using System.Threading.Tasks;
using HideAnyWindowManager.Models;
using HideAnyWindowManager.Services;
using Xunit;

public class ConfigStoreTests
{
    [Fact]
    public async Task RoundTripsConfigToDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"haw-test-{System.Guid.NewGuid():N}.json");
        var store = new ConfigStore(path);

        var cfg = new Config
        {
            ServiceState = "stopped",
            Hotkeys = new HotkeySettings
            {
                SuspendAll = "Ctrl+Alt+H",
                HideForeground = "Ctrl+Alt+Shift+H",
                UnhideLast = "Ctrl+Alt+Shift+U",
            },
            Rules =
            {
                new Rule
                {
                    Id = "magnify-exe",
                    Exe = "magnify.exe",
                    Name = "Magnifier",
                    Enabled = true,
                    Hotkey = "Ctrl+Alt+1",
                },
            },
        };
        await store.SaveImmediateAsync(cfg);

        var loaded = await store.LoadAsync();
        Assert.Equal("stopped", loaded.ServiceState);
        Assert.Single(loaded.Rules);
        Assert.Equal("magnify.exe", loaded.Rules[0].Exe);
        Assert.True(loaded.Rules[0].Enabled);
        Assert.Equal("Ctrl+Alt+1", loaded.Rules[0].Hotkey);
        Assert.Equal("Ctrl+Alt+H", loaded.Hotkeys.SuspendAll);
        Assert.Equal("Ctrl+Alt+Shift+H", loaded.Hotkeys.HideForeground);
        Assert.Equal("Ctrl+Alt+Shift+U", loaded.Hotkeys.UnhideLast);

        File.Delete(path);
    }

    [Fact]
    public async Task ReturnsDefaultsWhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"haw-missing-{System.Guid.NewGuid():N}.json");
        var store = new ConfigStore(path);

        var loaded = await store.LoadAsync();
        Assert.Equal("running", loaded.ServiceState);
        Assert.Empty(loaded.Rules);
        Assert.Equal("Ctrl+Alt+H", loaded.Hotkeys.SuspendAll);
    }

    [Fact]
    public async Task NormalizesHotkeysAndRepairsOlderConfig()
    {
        // Schema 1: no "hotkeys" object, and a rule with a non-canonical chord.
        var path = Path.Combine(Path.GetTempPath(), $"haw-old-{System.Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "schemaVersion": 1,
                  "serviceState": "running",
                  "rules": [
                    { "id": "magnify-exe", "exe": "magnify.exe", "name": "Magnifier",
                      "enabled": false, "hotkey": "shift+ctrl+alt+2" }
                  ]
                }
                """);

            var loaded = await new ConfigStore(path).LoadAsync();

            // Missing global hotkeys fall back to the model defaults...
            Assert.Equal("Ctrl+Alt+H", loaded.Hotkeys.SuspendAll);
            // ...and supplied chords are canonicalized so both processes agree.
            Assert.Equal("Ctrl+Alt+Shift+2", loaded.Rules[0].Hotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadsNumericBooleansWrittenByService()
    {
        // The AHK service serializes true/false as 1/0 because AutoHotkey has no
        // boolean type. The manager must still show those rules (a hotkey toggle
        // used to make the whole list look corrupt and empty).
        var path = Path.Combine(Path.GetTempPath(), $"haw-numeric-{System.Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "schemaVersion": 2,
                  "serviceState": "running",
                  "hotkeys": {
                    "suspendAll": "Ctrl+Alt+H",
                    "hideForeground": "Ctrl+Alt+Shift+H",
                    "unhideLast": "Ctrl+Alt+Shift+U"
                  },
                  "rules": [
                    { "id": "on", "exe": "on.exe", "name": "On", "enabled": 1, "hotkey": "Ctrl+Alt+1" },
                    { "id": "off", "exe": "off.exe", "name": "Off", "enabled": 0, "hotkey": "Ctrl+Alt+2" }
                  ]
                }
                """);

            var loaded = await new ConfigStore(path).LoadAsync();

            Assert.Equal(2, loaded.Rules.Count);
            Assert.True(loaded.Rules[0].Enabled);
            Assert.False(loaded.Rules[1].Enabled);
            Assert.Equal("Ctrl+Alt+2", loaded.Rules[1].Hotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
