using System.Runtime.InteropServices;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

/// <summary>One tray-menu entry; <see cref="Label"/> null means a separator.</summary>
internal sealed record TrayMenuItem(string? Label, Action? Action = null)
{
    public static readonly TrayMenuItem Separator = new(Label: null);
}

/// <summary>
/// Minimal Shell_NotifyIcon wrapper (no WinForms dependency).
/// The context menu is a native Win32 popup (TrackPopupMenuEx): a WPF ContextMenu opened from a
/// tray callback loses activation when Windows closes the "hidden icons" flyout and vanishes
/// instantly. The native menu runs its own modal loop and follows the system dark mode.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private readonly MessageWindow _window;
    private readonly IReadOnlyList<TrayMenuItem> _menu;
    private readonly uint _taskbarCreatedMsg;
    private readonly IntPtr _icon;
    private string _tooltip = "ZipDrop";
    private bool _added;
    private bool _menuOpen;

    static TrayIcon() => EnableDarkMenus();

    public TrayIcon(MessageWindow window, IReadOnlyList<TrayMenuItem> menu)
    {
        _window = window;
        _menu = menu;
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadAppIcon();
        _window.Message += OnMessage;
        Add();
    }

    /// <summary>Left click on the icon.</summary>
    public event Action? Activated;

    public string Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value.Length > 127 ? value[..127] : value;
            if (!_added) return;
            var data = CreateData(NIF_TIP);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    /// <summary>Windows toast-style balloon. Shown by Windows; ZipDrop does not keep it.</summary>
    public void ShowNotification(string title, string text, bool warning = false)
    {
        if (!_added) return;
        var data = CreateData(NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = (warning ? NIIF_WARNING : NIIF_INFO) | NIIF_NOSOUND;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA CreateData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMsg)
        {
            Add(); // Explorer restarted: our icon is gone, add it back.
            return false;
        }
        if (msg != CallbackMessage) return false;

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
                Activated?.Invoke();
                break;
            case WM_RBUTTONUP:
                ShowMenu();
                break;
        }
        return true;
    }

    private void ShowMenu()
    {
        if (_menuOpen) return;
        _menuOpen = true;
        var hMenu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < _menu.Count; i++)
            {
                if (_menu[i].Label is { } label) AppendMenu(hMenu, MF_STRING, (UIntPtr)(i + 1), label);
                else AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
            }

            GetCursorPos(out var pt);
            // Documented requirements for notification-area menus: the owner must be foreground while
            // the menu is up, and a dummy message afterwards lets the menu dismiss correctly next time.
            SetForegroundWindow(_window.Handle);
            var cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_BOTTOMALIGN,
                pt.X, pt.Y, _window.Handle, IntPtr.Zero);
            PostMessage(_window.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            // Run the action after we leave the window procedure (it may open dialogs).
            if (cmd > 0 && cmd <= _menu.Count && _menu[cmd - 1].Action is { } action)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(action);
        }
        finally
        {
            DestroyMenu(hMenu);
            _menuOpen = false;
        }
    }

    /// <summary>
    /// Opt the process into dark Win32 menus when Windows uses dark mode (uxtheme ordinals 135/136,
    /// used by Explorer, Notepad++, PowerToys…). Undocumented: ignored if unavailable.
    /// </summary>
    private static void EnableDarkMenus()
    {
        try
        {
            SetPreferredAppMode(1 /* AllowDark: follow system */);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }

    private static IntPtr LoadAppIcon()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return IntPtr.Zero;
        var small = new IntPtr[1];
        return ExtractIconEx(exe, 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
    }

    // ---- Menu interop (only used here) ----
    private const int WM_NULL = 0x0000;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
