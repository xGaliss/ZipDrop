using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

/// <summary>
/// Minimal Shell_NotifyIcon wrapper (no WinForms dependency) with a WPF context menu,
/// so the menu follows the app theme and the process stays light.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMsg;
    private readonly IntPtr _icon;
    private string _tooltip = "ZipDrop";
    private bool _added;

    public TrayIcon(MessageWindow window, ContextMenu menu)
    {
        _window = window;
        Menu = menu;
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadAppIcon();
        _window.Message += OnMessage;
        Add();
    }

    public ContextMenu Menu { get; }

    /// <summary>Left click / double click on the icon.</summary>
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
        // Required so the menu closes when the user clicks elsewhere (documented Shell_NotifyIcon quirk).
        SetForegroundWindow(_window.Handle);
        Menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        Menu.IsOpen = true;
        if (PresentationSource.FromVisual(Menu) is HwndSource source)
            SetForegroundWindow(source.Handle);
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
}
