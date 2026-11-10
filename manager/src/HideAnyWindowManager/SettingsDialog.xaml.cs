using System;
using HideAnyWindowManager.Util;
using HideAnyWindowManager.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HideAnyWindowManager;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly MainViewModel _viewModel;
    private readonly Action _onChanged;
    private bool _initializing = true;

    // 0 = not capturing, 1 = suspendAll, 2 = hideForeground, 3 = unhideLast.
    private int _capturing;

    public SettingsDialog(MainViewModel viewModel, Action onChanged)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onChanged = onChanged;
        AutostartSwitch.IsOn = App.AutostartManager.IsEnabled();
        RefreshHotkeyButtons();
        _initializing = false;
    }

    private void AutostartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (AutostartSwitch.IsOn)
            App.AutostartManager.TryEnable();
        else
            App.AutostartManager.TryDisable();
    }

    // ------------------------------------------------------------------
    // Inline hotkey capture. Deliberately not a nested ContentDialog:
    // WinUI allows only one open ContentDialog per XamlRoot.
    // ------------------------------------------------------------------

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        _capturing = button.Name switch
        {
            nameof(SuspendAllButton) => 1,
            nameof(HideForegroundButton) => 2,
            nameof(UnhideLastButton) => 3,
            _ => 0,
        };
        if (_capturing == 0) return;

        RefreshHotkeyButtons();
        button.Content = "Press keys\u2026";
        HotkeyErrorText.Visibility = Visibility.Collapsed;
        Focus(FocusState.Programmatic);
    }

    private void Dialog_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_capturing == 0)
            return;

        if (HotkeyCaptureKeys.IsModifier(e.Key))
            return;

        // Escape cancels capture instead of closing the dialog.
        if (e.Key == VirtualKey.Escape)
        {
            EndCapture();
            e.Handled = true;
            return;
        }

        // Backspace/Delete with no modifier clears the binding.
        if (!HotkeyCaptureKeys.AnyModifierDown()
            && e.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            ApplyCapture("");
            e.Handled = true;
            return;
        }

        // Tab/Enter/arrows keep their normal dialog behavior without modifiers.
        if (!HotkeyCaptureKeys.AnyModifierDown() && HotkeyCaptureKeys.IsUiKey(e.Key))
            return;

        if (!HotkeyCaptureKeys.TryBuild(e.Key, out var candidate))
        {
            if (!HotkeyCaptureKeys.AnyModifierDown() && HotkeyCaptureKeys.KeyName(e.Key) != null)
            {
                HotkeyErrorText.Text = "Hold at least one modifier (Ctrl, Alt, Shift or Win).";
                HotkeyErrorText.Visibility = Visibility.Visible;
                e.Handled = true;
            }
            return;
        }

        string? error = ValidateGlobal(candidate, _capturing);
        if (error != null)
        {
            HotkeyErrorText.Text = error;
            HotkeyErrorText.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }

        ApplyCapture(candidate);
        e.Handled = true;
    }

    private void ApplyCapture(string value)
    {
        switch (_capturing)
        {
            case 1: _viewModel.Hotkeys.SuspendAll = value; break;
            case 2: _viewModel.Hotkeys.HideForeground = value; break;
            case 3: _viewModel.Hotkeys.UnhideLast = value; break;
            default: return;
        }
        EndCapture();
        HotkeyErrorText.Visibility = Visibility.Collapsed;
        _onChanged();
    }

    private void EndCapture()
    {
        _capturing = 0;
        RefreshHotkeyButtons();
    }

    private void RefreshHotkeyButtons()
    {
        SuspendAllButton.Content = Describe(_viewModel.Hotkeys.SuspendAll);
        HideForegroundButton.Content = Describe(_viewModel.Hotkeys.HideForeground);
        UnhideLastButton.Content = Describe(_viewModel.Hotkeys.UnhideLast);
    }

    private static string Describe(string value)
        => string.IsNullOrWhiteSpace(value) ? "Not set" : value;

    private string? ValidateGlobal(string candidate, int which)
    {
        if (which != 1 && HotkeyText.Conflicts(candidate, _viewModel.Hotkeys.SuspendAll))
            return "Already used by Hide/show all apps.";
        if (which != 2 && HotkeyText.Conflicts(candidate, _viewModel.Hotkeys.HideForeground))
            return "Already used by Hide active window.";
        if (which != 3 && HotkeyText.Conflicts(candidate, _viewModel.Hotkeys.UnhideLast))
            return "Already used by Unhide last window.";

        foreach (var rule in _viewModel.Rules)
        {
            if (HotkeyText.Conflicts(candidate, rule.Hotkey))
                return $"Already used by {rule.Name}.";
        }
        return null;
    }
}
