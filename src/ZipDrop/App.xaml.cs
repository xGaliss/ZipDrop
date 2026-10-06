using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ZipDrop.Core.Baskets;
using ZipDrop.Core.Formatting;
using ZipDrop.Core.Settings;
using ZipDrop.Services;
using ZipDrop.UI;

namespace ZipDrop;

/// <summary>
/// Composition root. Owns every long-lived service; there is no DI container on purpose.
/// </summary>
public partial class App : Application
{
    private readonly SettingsStore _store = new();
    private readonly MemoryTrimmer _trimmer = new();
    private SingleInstance? _single;
    private AppSettings _settings = new();
    private Basket _basket = null!;
    private OverlayViewModel _vm = null!;
    private OverlayWindow _overlay = null!;
    private MessageWindow? _messages;
    private GlobalHotkey? _hotkey;
    private ShakeService? _shake;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        var paths = e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        _single = new SingleInstance();
        if (!_single.IsFirst)
        {
            _single.Forward(paths);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => { Log(args.Exception); args.SetObserved(); };

        ThemeService.Initialize();
        var firstRun = !File.Exists(_store.FilePath);
        _settings = _store.Load();

        _basket = new Basket();
        _vm = new OverlayViewModel(_basket, () => _settings);
        _overlay = new OverlayWindow(_vm);
        _vm.CloseRequested += () => _overlay.HideAnimated();
        _vm.SettingsRequested += OpenSettings;
        _vm.ZipFinished += (message, isError) =>
        {
            if (!_overlay.IsVisible) _tray?.ShowNotification(isError ? Strings.ErrorTitle : Strings.ZipCreated, message, isError);
        };

        // Trim memory whenever ZipDrop goes back to idling in the tray.
        _overlay.IsVisibleChanged += (_, _) =>
        {
            if (_overlay.IsVisible || _vm.IsBusy) _trimmer.Cancel();
            else _trimmer.Schedule();
        };

        _messages = new MessageWindow("ZipDrop.Messages");
        _hotkey = new GlobalHotkey(_messages);
        _hotkey.Pressed += () => _overlay.Toggle();
        if (firstRun) _settings = _settings with { GlobalShortcut = PickDefaultShortcut() };

        _shake = new ShakeService();
        _shake.ShakeDetected += (x, y) => Dispatcher.BeginInvoke(() => _overlay.SummonForDrag(x, y));
        _shake.DragEnded += fired => { if (fired) Dispatcher.BeginInvoke(_overlay.OnGlobalDragEnded); };

        _tray = new TrayIcon(_messages, BuildTrayMenu());
        _tray.Activated += () => _overlay.Toggle();
        _basket.PropertyChanged += (_, _) => UpdateTooltip();
        UpdateTooltip();

        var result = ApplySettings(_settings, persist: firstRun, force: true);
        if (result.ShortcutError is not null)
            _tray.ShowNotification(Strings.ShortcutUnavailableTitle, result.ShortcutError + " " + Strings.ChangeInSettings, warning: true);
        else if (firstRun)
            _tray.ShowNotification(Strings.RunningTitle, Strings.RunningText(_settings.GlobalShortcut));

        _single.Listen(request => Dispatcher.BeginInvoke(() => HandleRequest(request)));

        if (paths.Count > 0 || !e.Args.Contains(StartupRegistration.BackgroundArg, StringComparer.OrdinalIgnoreCase))
            HandleRequest(paths);
        else
            _trimmer.Schedule();
    }

    /// <summary>Launch request (from this or a later instance): show, optionally adding paths.</summary>
    private void HandleRequest(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0) _vm.AddPaths(paths);
        _overlay.ShowNearCursor();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shake?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _messages?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    // ---------- Settings ----------

    private SettingsApplyResult ApplySettings(AppSettings next, bool persist = true, bool force = false)
    {
        string? shortcutError = null, shakeError = null;

        if (force || next.GlobalShortcut != _settings.GlobalShortcut || _hotkey!.Current is null)
        {
            if (!HotkeyGesture.TryParse(next.GlobalShortcut, out var gesture))
            {
                shortcutError = Strings.ShortcutInvalid(next.GlobalShortcut);
            }
            else if (!_hotkey!.Register(gesture))
            {
                shortcutError = Strings.ShortcutTaken(gesture.ToString());
                if (!force && HotkeyGesture.TryParse(_settings.GlobalShortcut, out var previous)) _hotkey.Register(previous);
            }
            if (shortcutError is not null && !force) next = next with { GlobalShortcut = _settings.GlobalShortcut };
        }

        _shake!.Configure(next.ShakeEnabled, next.ShakeSensitivity);
        DevLog.Write($"Settings applied: shake={next.ShakeEnabled}/{next.ShakeSensitivity} hook={_shake.IsEnabled} hotkey={_hotkey!.Current}");
        if (next.ShakeEnabled && !_shake.IsEnabled)
            shakeError = Strings.ShakeUnavailable;

        StartupRegistration.Apply(next.LaunchAtStartup);

        _settings = next;
        if (persist)
        {
            try { _store.Save(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log(ex); }
        }
        return new SettingsApplyResult(shortcutError, shakeError);
    }

    /// <summary>
    /// Default shortcut for NEW installs (existing settings are never changed). Ctrl+Alt is AltGr on many
    /// layouts (Polish AltGr+Z = "ż"), so a candidate that types a character, or that another app owns,
    /// is skipped. Ctrl+Shift+Z (Redo in many apps) is the last resort.
    /// </summary>
    private string PickDefaultShortcut()
    {
        foreach (var candidate in new[] { "Ctrl+Alt+Z", "Win+Shift+Z", "Ctrl+Shift+Z" })
        {
            if (!HotkeyGesture.TryParse(candidate, out var g) || g.ProducesCharacter()) continue;
            if (!_hotkey!.Register(g)) continue;
            _hotkey.Unregister();
            DevLog.Write($"Default shortcut: {candidate}");
            return candidate;
        }
        return new AppSettings().GlobalShortcut;
    }

    private void SuspendHotkey(bool suspend)
    {
        if (suspend) _hotkey!.Unregister();
        else if (HotkeyGesture.TryParse(_settings.GlobalShortcut, out var g)) _hotkey!.Register(g);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, s => ApplySettings(s), SuspendHotkey);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ---------- Tray ----------

    private TrayMenuItem[] BuildTrayMenu() =>
    [
        new(Strings.MenuOpen, () => _overlay.ShowNearCursor(activate: true)),
        new(Strings.MenuNewBasket, NewBasket),
        new(Strings.MenuSettings, OpenSettings),
        TrayMenuItem.Separator,
        new(Strings.MenuExit, ExitApp),
    ];

    private void UpdateTooltip()
    {
        if (_tray is null) return;
        _tray.Tooltip = _basket.IsEmpty
            ? Strings.TrayEmpty
            : Strings.TrayItems(Strings.ItemCount(_basket.Count), SizeFormatter.Format(_basket.TotalBytes, Strings.Culture));
    }

    private void NewBasket()
    {
        if (_vm.IsBusy) return;
        if (!_basket.IsEmpty)
        {
            var answer = MessageBox.Show(
                Strings.NewBasketConfirm(Strings.ItemCount(_basket.Count)),
                "ZipDrop", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }
        _vm.NewBasket();
        _overlay.ShowNearCursor(activate: true);
    }

    private void ExitApp()
    {
        if (_vm.IsBusy)
        {
            var answer = MessageBox.Show(Strings.ExitWhileZipping, "ZipDrop",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
            _vm.CancelZip();
        }
        Shutdown();
    }

    // ---------- Errors (local log only, never sent anywhere) ----------

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        MessageBox.Show(Strings.UnexpectedError(e.Exception.Message), "ZipDrop",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal static void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipDrop");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }
}
