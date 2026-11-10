using System;
using HideAnyWindowManager.Util;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace HideAnyWindowManager;

/// <summary>
/// Captures one modifier+key chord. The caller applies the result:
/// Primary = <see cref="Hotkey"/>, Secondary = clear (empty), None = cancel.
/// </summary>
public sealed partial class HotkeyCaptureDialog : ContentDialog
{
    private readonly Func<string, string?>? _validate;
    private string _hotkey;

    public string Hotkey => _hotkey;

    /// <param name="initial">Existing chord; invalid/empty values produce a blank capture.</param>
    /// <param name="validate">Optional: returns an error message when the chord conflicts.</param>
    public HotkeyCaptureDialog(string? initial, Func<string, string?>? validate = null)
    {
        InitializeComponent();
        _hotkey = HotkeyText.Normalize(initial);
        _validate = validate;
        RefreshDisplay();
        Opened += (_, __) => Focus(FocusState.Programmatic);
    }

    private void Dialog_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HotkeyCaptureKeys.IsModifier(e.Key))
            return;                         // wait for the actual key

        // Bare navigation/confirmation keys belong to the dialog UI.
        if (!HotkeyCaptureKeys.AnyModifierDown() && HotkeyCaptureKeys.IsUiKey(e.Key))
            return;

        if (!HotkeyCaptureKeys.TryBuild(e.Key, out var candidate))
        {
            if (!HotkeyCaptureKeys.AnyModifierDown() && HotkeyCaptureKeys.KeyName(e.Key) != null)
            {
                ErrorText.Text = "Hold at least one modifier (Ctrl, Alt, Shift or Win).";
                ErrorText.Visibility = Visibility.Visible;
                e.Handled = true;
            }
            return;
        }

        SetCandidate(candidate);
        e.Handled = true;
    }

    private void SetCandidate(string candidate)
    {
        if (!HotkeyText.TryNormalize(candidate, out var normalized))
            return;

        _hotkey = normalized;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        ComboText.Text = _hotkey.Length == 0 ? "Press keys\u2026" : _hotkey;

        string? error = _hotkey.Length == 0 ? null : _validate?.Invoke(_hotkey);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        IsPrimaryButtonEnabled = _hotkey.Length > 0 && error == null;
    }
}
