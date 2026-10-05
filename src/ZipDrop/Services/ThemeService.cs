using System.Windows;
using Microsoft.Win32;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

/// <summary>Follows the Windows light/dark app theme by swapping the brush dictionary.</summary>
internal static class ThemeService
{
    private static ResourceDictionary? _current;

    public static bool IsDark { get; private set; } = true;

    public static void Initialize()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                Application.Current.Dispatcher.BeginInvoke(Apply);
        };
    }

    private static void Apply()
    {
        IsDark = ReadIsDark();
        var uri = new Uri($"pack://application:,,,/ZipDrop;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml");
        var dict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;

        foreach (Window w in Application.Current.Windows)
            ApplyTitleBar(w);
    }

    /// <summary>Dark/light native title bar for standard windows (Settings).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private static bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light ? light == 0 : true;
        }
        catch { return true; }
    }
}
