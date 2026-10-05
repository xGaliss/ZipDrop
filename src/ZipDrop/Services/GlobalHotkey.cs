using System.Windows.Input;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

/// <summary>A modifier+key combination such as "Ctrl+Shift+Z".</summary>
internal readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (key is not null) return false;
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (Enum.TryParse<Key>(raw, ignoreCase: true, out var k)) key = k;
                    else return false;
                    break;
            }
        }

        if (key is null || !IsValid(mods, key.Value)) return false;
        gesture = new HotkeyGesture(mods, key.Value);
        return true;
    }

    /// <summary>Needs Ctrl, Alt or Win, and a non-modifier key.</summary>
    public static bool IsValid(ModifierKeys mods, Key key) =>
        (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0
        && key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key is >= Key.D0 and <= Key.D9 ? ((int)(Key - Key.D0)).ToString() : Key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>One system-wide hotkey via RegisterHotKey (no keyboard hook needed).</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5A44; // "ZD"
    private readonly MessageWindow _window;
    private bool _registered;

    public GlobalHotkey(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    public event Action? Pressed;

    public HotkeyGesture? Current { get; private set; }

    /// <summary>Replaces the current hotkey. Returns false if another app already owns it.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        uint mods = MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);

        _registered = RegisterHotKey(_window.Handle, HotkeyId, mods, vk);
        Current = _registered ? gesture : null;
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_HOTKEY || (int)wParam != HotkeyId) return false;
        Pressed?.Invoke();
        return true;
    }

    public void Dispose()
    {
        Unregister();
        _window.Message -= OnMessage;
    }
}
