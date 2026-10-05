using System.Windows;
using System.Windows.Input;
using ZipDrop.Core.Gestures;
using ZipDrop.Core.Settings;
using ZipDrop.Services;

namespace ZipDrop.UI;

internal sealed record SettingsApplyResult(string? ShortcutError = null, string? ShakeError = null);

/// <summary>
/// Tiny settings window. Changes apply instantly (no Save button), PowerToys-style.
/// The host decides how to apply them and reports failures back.
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, SettingsApplyResult> _apply;
    private readonly Action<bool> _suspendHotkey;
    private AppSettings _settings;
    private bool _loading;
    private bool _capturing;

    public SettingsWindow(AppSettings current, Func<AppSettings, SettingsApplyResult> apply, Action<bool> suspendHotkey)
    {
        InitializeComponent();
        _settings = current;
        _apply = apply;
        _suspendHotkey = suspendHotkey;

        _loading = true;
        LaunchAtStartup.IsChecked = current.LaunchAtStartup;
        ShakeEnabled.IsChecked = current.ShakeEnabled;
        ClearAfterZip.IsChecked = current.ClearBasketAfterZip;
        (current.ShakeSensitivity switch
        {
            ShakeSensitivity.Low => SensLow,
            ShakeSensitivity.High => SensHigh,
            _ => SensMedium,
        }).IsChecked = true;
        ShortcutText.Text = current.GlobalShortcut;
        VersionText.Text = $"ZipDrop {AppInfo.Version}";
        _loading = false;

        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        Closed += (_, _) => { if (_capturing) _suspendHotkey(false); };
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Commit(_settings with
        {
            LaunchAtStartup = LaunchAtStartup.IsChecked == true,
            ShakeEnabled = ShakeEnabled.IsChecked == true,
            ClearBasketAfterZip = ClearAfterZip.IsChecked == true,
            ShakeSensitivity = SensLow.IsChecked == true ? ShakeSensitivity.Low
                : SensHigh.IsChecked == true ? ShakeSensitivity.High
                : ShakeSensitivity.Medium,
        });
    }

    private void Commit(AppSettings next)
    {
        var result = _apply(next);
        if (result.ShortcutError is null) _settings = next;
        else _settings = next with { GlobalShortcut = _settings.GlobalShortcut };
        ShowError(ShortcutError, result.ShortcutError);
        ShowError(ShakeError, result.ShakeError);
        ShortcutText.Text = _settings.GlobalShortcut;
    }

    private static void ShowError(System.Windows.Controls.TextBlock target, string? message)
    {
        target.Text = message ?? "";
        target.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Opens the link in the user's browser (ZipDrop itself never goes online).</summary>
    private void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        e.Handled = true;
    }

    // ---------- Shortcut capture ----------

    private void ShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing) return;
        _capturing = true;
        _suspendHotkey(true); // otherwise the current hotkey would swallow the keystroke
        ShortcutText.Text = "Press keys…";
        ShortcutButton.Focus();
    }

    private void ShortcutButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            EndCapture();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            ShortcutText.Text = new HotkeyGesture(mods, Key.None).ToString().Replace("None", "…");
            return;
        }

        if (!HotkeyGesture.IsValid(mods, key))
        {
            ShowError(ShortcutError, "Use Ctrl, Alt or Win plus a key.");
            return;
        }

        var gesture = new HotkeyGesture(mods, key);
        _capturing = false;
        _suspendHotkey(false);
        Commit(_settings with { GlobalShortcut = gesture.ToString() });
    }

    private void ShortcutButton_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturing) EndCapture();
    }

    private void EndCapture()
    {
        _capturing = false;
        _suspendHotkey(false);
        ShortcutText.Text = _settings.GlobalShortcut;
    }
}
