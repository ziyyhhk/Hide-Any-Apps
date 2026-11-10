using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HideAnyWindowManager.Models;
using HideAnyWindowManager.Util;

namespace HideAnyWindowManager.Services;

public sealed class ConfigStore
{
    /// <summary>Cross-process write gate shared with the AHK service (config.ahk).</summary>
    private const string ConfigLockName = "HideAnyWindow_ConfigLock";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Source-generated metadata (see ConfigJsonContext) so trimming can strip
        // the reflection-based serializer.
        TypeInfoResolver = ConfigJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _debounceLock = new();
    private CancellationTokenSource? _debounceCts;
    private Config? _pendingConfig;

    public string ConfigPath => _path;

    public ConfigStore() : this(DefaultPath()) { }
    public ConfigStore(string path) { _path = path; }

    public static string DefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "HideAnyWindow", "config.json");

    public async Task<Config> LoadAsync()
    {
        if (!File.Exists(_path))
            return new Config();
        try
        {
            // Share everything: the service replaces this file with a rename when
            // a hotkey toggles a rule, and a stricter share mode would make that
            // rename fail for the ~milliseconds this read takes.
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var cfg = await JsonSerializer.DeserializeAsync<Config>(fs, JsonOpts).ConfigureAwait(false);
            return Normalize(cfg ?? new Config());
        }
        catch (JsonException)
        {
            // Malformed JSON. Preserve the user's (hand-recoverable) file before a later
            // save overwrites it with defaults, then fall back to defaults.
            TryBackupCorruptFile();
            return new Config();
        }
    }

    /// <summary>Repairs nulls and invalid hotkey strings from hand-edited or older configs.</summary>
    private static Config Normalize(Config cfg)
    {
        cfg.Hotkeys ??= new HotkeySettings();
        cfg.Hotkeys.SuspendAll = HotkeyText.Normalize(cfg.Hotkeys.SuspendAll);
        cfg.Hotkeys.HideForeground = HotkeyText.Normalize(cfg.Hotkeys.HideForeground);
        cfg.Hotkeys.UnhideLast = HotkeyText.Normalize(cfg.Hotkeys.UnhideLast);

        cfg.Rules ??= new();
        foreach (var rule in cfg.Rules)
            rule.Hotkey = HotkeyText.Normalize(rule.Hotkey);

        return cfg;
    }

    /// <summary>Schedules a save for 200ms after the last call. Subsequent calls supersede
    /// the prior schedule. Call <see cref="FlushPendingAsync"/> on shutdown so a save still
    /// inside the debounce window isn't lost.</summary>
    public void ScheduleSave(Config cfg)
    {
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            var cts = new CancellationTokenSource();
            _debounceCts = cts;
            _pendingConfig = cfg;
            _ = RunDebouncedSaveAsync(cfg, cts.Token);
        }
    }

    private async Task RunDebouncedSaveAsync(Config cfg, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token).ConfigureAwait(false);
            await SaveImmediateAsync(cfg).ConfigureAwait(false);
            lock (_debounceLock)
            {
                if (ReferenceEquals(_pendingConfig, cfg))
                    _pendingConfig = null;
            }
        }
        catch (OperationCanceledException) { /* superseded by a newer ScheduleSave */ }
    }

    /// <summary>Writes any pending debounced save immediately, skipping the remaining
    /// debounce delay. Call on window close so the last edit is guaranteed to persist.
    /// Uses ConfigureAwait(false) throughout so a synchronous caller can block on it
    /// from the UI thread without deadlocking.</summary>
    public async Task FlushPendingAsync()
    {
        Config? cfg;
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            cfg = _pendingConfig;
            _pendingConfig = null;
        }
        if (cfg != null)
            await SaveImmediateAsync(cfg).ConfigureAwait(false);
    }

    public async Task SaveImmediateAsync(Config cfg)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";

            // The service can rewrite config.json (hotkey toggles). Serialize
            // both writers through a shared named semaphore. A semaphore, not
            // a mutex: async continuations may resume on a different thread,
            // and mutex ownership is thread-affine.
            using var gate = new Semaphore(1, 1, ConfigLockName);
            bool held = false;
            try
            {
                held = gate.WaitOne(TimeSpan.FromSeconds(2));
                if (!held)
                    ManagerLog.Write("ConfigStore: config lock timed out — writing anyway");

                using (var fs = File.Create(tmp))
                {
                    await JsonSerializer.SerializeAsync(fs, cfg, JsonOpts).ConfigureAwait(false);
                    await fs.FlushAsync().ConfigureAwait(false);
                    fs.Flush(flushToDisk: true);   // force bytes to disk before the rename
                }

                // File.Move with overwrite=true is atomic on the same volume. The AHK service
                // briefly opens config.json on its ~1Hz poll, which can lock it momentarily;
                // retry a few times so a transient lock doesn't lose the write.
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(tmp, _path, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < 4)
                    {
                        await Task.Delay(40).ConfigureAwait(false);
                    }
                    catch (UnauthorizedAccessException) when (attempt < 4)
                    {
                        await Task.Delay(40).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (held)
                    gate.Release();
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = Path.Combine(
                Path.GetDirectoryName(_path)!,
                Path.GetFileNameWithoutExtension(_path) + ".corrupt.json");
            // Keep the first corrupt snapshot only — don't clobber it on repeated loads.
            if (!File.Exists(backup))
                File.Copy(_path, backup);
        }
        catch { /* best effort — never let a backup failure mask the load */ }
    }
}
