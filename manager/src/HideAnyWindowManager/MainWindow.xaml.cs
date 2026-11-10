using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HideAnyWindowManager.Models;
using HideAnyWindowManager.Services;
using HideAnyWindowManager.Util;
using HideAnyWindowManager.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HideAnyWindowManager;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private Microsoft.UI.Xaml.DispatcherTimer? _statusTimer;
    private HideAnyWindowManager.Util.WindowMinSize? _minSize;
    private Windows.UI.ViewManagement.UISettings? _uiSettings;

    private FileSystemWatcher? _configWatcher;
    private DateTime _suppressExternalReloadUntilUtc = DateTime.MinValue;
    private bool? _lastEffectiveStatus;

    private readonly SolidColorBrush _runningBrush = new(Color.FromArgb(0xFF, 0x2E, 0x9C, 0x4F));
    private readonly SolidColorBrush _stoppedBrush = new(Color.FromArgb(0xFF, 0x88, 0x88, 0x88));

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        TrySetWindowIcon();
        TryRemoveWindowBorder();
        ApplyMinSize();
        ResizeToDefault();
        RulesList.ItemsSource = ViewModel.Rules;
        ViewModel.Rules.CollectionChanged += (_, __) => UpdateEmptyState();
        UpdateEmptyState();
        _ = LoadAsync();
        StartStatusWatch();
        StartConfigWatch();
        HookThemeChange();
        Closed += OnClosed;
    }

    private void UpdateEmptyState()
        => EmptyState.Visibility = ViewModel.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _statusTimer?.Stop();
        _configWatcher?.Dispose();
        // Guarantee a debounced save still inside its 200ms window is written before exit.
        try { App.ConfigStore.FlushPendingAsync().GetAwaiter().GetResult(); }
        catch { /* best effort on shutdown */ }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            // Look for AppIcon.ico next to the executable (works for both
            // dev and self-contained publish layouts).
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch { /* non-fatal */ }
    }

    private void HookThemeChange()
    {
        try
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += (s, a) =>
            {
                DispatcherQueue.TryEnqueue(UpdateForCurrentTheme);
            };
            UpdateForCurrentTheme();
        }
        catch { /* best-effort; falls back to whatever theme XAML picked at load */ }
    }

    private void UpdateForCurrentTheme()
    {
        if (_uiSettings is null) return;
        var bg = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        // Background is near-white in light mode, near-black in dark mode.
        bool isDark = (bg.R + bg.G + bg.B) < 384;

        var theme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        if (RootGrid != null) RootGrid.RequestedTheme = theme;

        try
        {
            var tb = AppWindow.TitleBar;
            var fg = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var transparent = Microsoft.UI.Colors.Transparent;
            var hoverBg = isDark ? Color.FromArgb(255, 60, 60, 60) : Color.FromArgb(255, 230, 230, 230);
            var pressedBg = isDark ? Color.FromArgb(255, 80, 80, 80) : Color.FromArgb(255, 210, 210, 210);
            var inactiveFg = Color.FromArgb(255, 130, 130, 130);

            tb.ButtonForegroundColor = fg;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = inactiveFg;

            tb.ButtonBackgroundColor = transparent;
            tb.ButtonInactiveBackgroundColor = transparent;
            tb.ButtonHoverBackgroundColor = hoverBg;
            tb.ButtonPressedBackgroundColor = pressedBg;
        }
        catch { /* TitleBar customization not available in some configurations */ }
    }

    private void ApplyMinSize()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _minSize = HideAnyWindowManager.Util.WindowMinSize.Apply(hwnd, 480, 420);
        }
        catch { /* non-fatal — window will simply be resizable to anything */ }
    }

    private void ResizeToDefault()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = HideAnyWindowManager.Util.Win32.GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            int w = (int)(480 * dpi / 96.0);
            int h = (int)(420 * dpi / 96.0);

            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var work = displayArea.WorkArea;
            int x = work.X + (work.Width - w) / 2;
            int y = work.Y + (work.Height - h) / 2;

            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        }
        catch { /* non-fatal */ }
    }

    private void TryRemoveWindowBorder()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint color = HideAnyWindowManager.Util.Win32.DWMWA_COLOR_NONE;
            HideAnyWindowManager.Util.Win32.DwmSetWindowAttribute(
                hwnd,
                HideAnyWindowManager.Util.Win32.DWMWA_BORDER_COLOR,
                ref color,
                sizeof(uint));
        }
        catch
        {
            // DWMWA_BORDER_COLOR requires Windows 11; ignore silently on older versions.
        }
    }

    private void StartStatusWatch()
    {
        _statusTimer = new Microsoft.UI.Xaml.DispatcherTimer
        {
            Interval = System.TimeSpan.FromSeconds(1),
        };
        _statusTimer.Tick += (_, __) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        // Only the mutex needs polling here: the service normally leaves
        // config.json alone, but a hotkey toggle now rewrites a rule's
        // enabled flag. Those changes arrive through the file watcher, not
        // this timer, so poll just the cheap mutex check.
        bool mutex = App.ServiceController.IsServiceRunning();
        bool effective = mutex && ViewModel.ServiceState == "running";
        if (_lastEffectiveStatus == effective)
            return;

        _lastEffectiveStatus = effective;
        ViewModel.IsServiceRunning = effective;

        StatusDot.Fill = effective ? _runningBrush : _stoppedBrush;
        StatusLabel.Text = ViewModel.StatusText;
        ServiceButton.Content = ViewModel.ServiceButtonText;
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var cfg = await App.ConfigStore.LoadAsync();
        ViewModel.LoadFrom(cfg);
        foreach (var rvm in ViewModel.Rules)
            _ = LoadIconForRuleAsync(rvm);
    }

    private async System.Threading.Tasks.Task LoadIconForRuleAsync(RuleViewModel rvm)
    {
        if (string.IsNullOrEmpty(rvm.Path)) return;
        rvm.Icon = await IconHelper.LoadIconAsync(rvm.Path);
    }

    // ------------------------------------------------------------------
    // External config changes
    // ------------------------------------------------------------------
    // The service now owns the per-app hotkey toggles, so config.json can be
    // rewritten by another process while this window is open. Watch the file
    // and mirror those changes into the UI instead of clobbering them with
    // the next save.

    private void StartConfigWatch()
    {
        try
        {
            var path = App.ConfigStore.ConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            _configWatcher = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                             | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _configWatcher.Changed += OnConfigFileChanged;
            _configWatcher.Created += OnConfigFileChanged;
            _configWatcher.Renamed += OnConfigFileChanged;
        }
        catch (Exception ex)
        {
            ManagerLog.Write($"config watcher failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Our own atomic save raises Changed/Created/Renamed too. Skip those,
        // but re-check after the window in case a real external write lands
        // while our save is settling.
        if (DateTime.UtcNow < _suppressExternalReloadUntilUtc)
        {
            _ = RecheckConfigAfterAsync(_suppressExternalReloadUntilUtc - DateTime.UtcNow);
            return;
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ReloadIfChangedAsync(); }
            catch (Exception ex)
            {
                ManagerLog.Write($"external reload failed: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    private async Task RecheckConfigAfterAsync(TimeSpan delay)
    {
        try
        {
            int ms = Math.Clamp((int)delay.TotalMilliseconds, 0, 5000);
            await Task.Delay(ms);
            DispatcherQueue.TryEnqueue(async () =>
            {
                try { await ReloadIfChangedAsync(); }
                catch (Exception ex)
                {
                    ManagerLog.Write($"external reload failed: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
        catch { /* window shutting down */ }
    }

    private async Task ReloadIfChangedAsync()
    {
        var cfg = await App.ConfigStore.LoadAsync();
        if (!ConfigDiffersFromViewModel(cfg))
            return;

        var selectedId = ViewModel.SelectedRule?.Id;
        ViewModel.LoadFrom(cfg);
        foreach (var rvm in ViewModel.Rules)
            _ = LoadIconForRuleAsync(rvm);

        if (selectedId != null)
            RulesList.SelectedItem = ViewModel.Rules.FirstOrDefault(r => r.Id == selectedId);
        else
            ViewModel.SelectedRule = null;

        ManagerLog.Write("config.json changed externally — reloaded UI");
    }

    private bool ConfigDiffersFromViewModel(Config cfg)
    {
        if (!string.Equals(cfg.ServiceState, ViewModel.ServiceState, StringComparison.Ordinal))
            return true;
        if (cfg.Rules.Count != ViewModel.Rules.Count)
            return true;

        var uiHotkeys = ViewModel.Hotkeys;
        if (!string.Equals(cfg.Hotkeys.SuspendAll, uiHotkeys.SuspendAll, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(cfg.Hotkeys.HideForeground, uiHotkeys.HideForeground, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(cfg.Hotkeys.UnhideLast, uiHotkeys.UnhideLast, StringComparison.OrdinalIgnoreCase))
            return true;

        for (int i = 0; i < cfg.Rules.Count; i++)
        {
            var disk = cfg.Rules[i];
            var ui = ViewModel.Rules[i];
            if (disk.Id != ui.Id
                || disk.Enabled != ui.Enabled
                || !string.Equals(disk.Hotkey, ui.Hotkey, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private void SuppressExternalReload(int milliseconds = 1500)
        => _suppressExternalReloadUntilUtc = DateTime.UtcNow.AddMilliseconds(milliseconds);

    private void RulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedRule = RulesList.SelectedItem as RuleViewModel;
    }

    private async void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not RuleViewModel rvm)
            return;

        var ruleId = rvm.Id;
        var dialog = new HotkeyCaptureDialog(rvm.Hotkey,
            candidate => ValidateRuleHotkey(candidate, ruleId))
        {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            RequestedTheme = RootGrid.RequestedTheme,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;

        // The config watcher may have rebuilt the rows while the dialog was open
        // (a hotkey toggle in the service). Apply to the current row, not the old
        // view-model instance, or the edit would be dropped on the next save.
        var target = ViewModel.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (target == null)
        {
            ManagerLog.Write($"hotkey ruleId={ruleId} vanished before apply");
            return;
        }

        target.Hotkey = result == ContentDialogResult.Primary ? dialog.Hotkey : "";
        ManagerLog.Write($"hotkey ruleId={ruleId} -> '{target.Hotkey}'");
        SaveDebounced();
    }

    private string? ValidateRuleHotkey(string candidate, string ruleId)
    {
        foreach (var rule in ViewModel.Rules)
        {
            if (rule.Id != ruleId && HotkeyText.Conflicts(candidate, rule.Hotkey))
                return $"Already used by {rule.Name}.";
        }
        if (HotkeyText.Conflicts(candidate, ViewModel.Hotkeys.SuspendAll))
            return "Already used by Hide/show all apps.";
        if (HotkeyText.Conflicts(candidate, ViewModel.Hotkeys.HideForeground))
            return "Already used by Hide active window.";
        if (HotkeyText.Conflicts(candidate, ViewModel.Hotkeys.UnhideLast))
            return "Already used by Unhide last window.";
        return null;
    }

    private void RuleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // IsOn is bound OneWay (display only), so this handler is the single writer of
        // Enabled. The guard ignores the Toggled that fires when the binding itself pushes
        // a programmatic value, avoiding a redundant save / feedback loop.
        if (sender is ToggleSwitch ts && ts.DataContext is RuleViewModel rvm && rvm.Enabled != ts.IsOn)
        {
            rvm.Enabled = ts.IsOn;
            ManagerLog.Write($"toggle ruleId={rvm.Id} -> Enabled={rvm.Enabled}");
            SaveDebounced();
        }
    }

    private void DeleteRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is RuleViewModel rvm)
        {
            ViewModel.Rules.Remove(rvm);
            ManagerLog.Write($"removed rule ruleId={rvm.Id}");
            SaveDebounced();
        }
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var existingExes = ViewModel.Rules.Select(r => r.Exe).ToList();
        var dialog = new AddPickerDialog(App.ProcessEnumerator, existingExes)
        {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            RequestedTheme = RootGrid.RequestedTheme,
        };
        var result = await dialog.ShowAsync();
        if (dialog.WasConfirmed(result) && dialog.SelectedProcess is { } proc)
        {
            var newRvm = new RuleViewModel(new Rule
            {
                Id = Rule.IdFromExe(proc.Exe),
                Exe = proc.Exe,
                Path = proc.FullPath,
                Name = proc.Name,
                Enabled = false,
                // Give every new rule a sensible default bind (Ctrl+Alt+1..9)
                // so the feature is discoverable; the user can rebind or clear it.
                Hotkey = HotkeyText.NextFreeSlot(UsedHotkeys()),
            })
            { ServiceRunning = ViewModel.IsServiceRunning };
            ViewModel.Rules.Add(newRvm);
            _ = LoadIconForRuleAsync(newRvm);
            SaveDebounced();
        }
    }

    private System.Collections.Generic.IEnumerable<string?> UsedHotkeys()
    {
        foreach (var rule in ViewModel.Rules)
            yield return rule.Hotkey;
        yield return ViewModel.Hotkeys.SuspendAll;
        yield return ViewModel.Hotkeys.HideForeground;
        yield return ViewModel.Hotkeys.UnhideLast;
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(ViewModel, SaveDebounced)
        {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            RequestedTheme = RootGrid.RequestedTheme,
        };
        await dlg.ShowAsync();
    }

    private async void ServiceButton_Click(object sender, RoutedEventArgs e)
    {
        var cfg = await App.ConfigStore.LoadAsync();
        bool mutex = App.ServiceController.IsServiceRunning();

        if (!mutex)
        {
            // Process not running -> ensure state is "running" then launch.
            cfg.ServiceState = "running";
            ViewModel.ServiceState = "running";
            SuppressExternalReload(2500);
            await App.ConfigStore.SaveImmediateAsync(cfg);
            ManagerLog.Write("ServiceButton: launching (mutex absent, set state=running)");
            if (!App.ServiceController.TryStartService())
            {
                var script = HideAnyWindowManager.Services.ServiceController.DefaultScriptPath();
                var ahkUia = HideAnyWindowManager.Services.ServiceController.DefaultAhkUiaPath();
                var dlg = new ContentDialog
                {
                    Title = "Couldn't start service",
                    Content = $"One of these wasn't found:\n\n  Service exe:  {ahkUia}\n  Service script:  {script}\n\nVerify both paths exist.",
                    CloseButtonText = "OK",
                    XamlRoot = ((FrameworkElement)Content).XamlRoot,
                    RequestedTheme = RootGrid.RequestedTheme,
                };
                await dlg.ShowAsync();
            }
        }
        else
        {
            // Process running -> ask it to shut down. The service polls config.json at ~1Hz;
            // on seeing "stopped" it restores every hidden window, releases the mutex and
            // exits, so it leaves no resident process behind.
            cfg.ServiceState = "stopped";
            ViewModel.ServiceState = "stopped";
            SuppressExternalReload(2500);
            await App.ConfigStore.SaveImmediateAsync(cfg);
            ManagerLog.Write("ServiceButton: stopping (mutex held, state -> stopped; service will exit)");
        }
        RefreshStatus();
    }

    private void SaveDebounced()
    {
        var cfg = ViewModel.ToConfig();
        var ruleSummary = string.Join(", ", cfg.Rules.Select(r => $"{r.Id}={(r.Enabled ? "on" : "off")}"));
        ManagerLog.Write($"SaveDebounced state={cfg.ServiceState} rules=[{ruleSummary}]");
        SuppressExternalReload();
        App.ConfigStore.ScheduleSave(cfg);
    }
}
