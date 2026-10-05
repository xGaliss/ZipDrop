using System.Windows.Interop;

namespace ZipDrop.Services;

/// <summary>
/// Hidden top-level window used as the target for WM_HOTKEY and tray-icon callbacks.
/// (Not HWND_MESSAGE: message-only windows miss the "TaskbarCreated" broadcast that
/// tells us to re-add the tray icon after Explorer restarts.)
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow(string name)
    {
        _source = new HwndSource(new HwndSourceParameters(name)
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0, // WS_OVERLAPPED, never shown
        });
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>(msg, wParam, lParam) -> handled.</summary>
    public event Func<int, IntPtr, IntPtr, bool>? Message;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var handlers = Message;
        if (handlers is null) return IntPtr.Zero;
        foreach (Func<int, IntPtr, IntPtr, bool> h in handlers.GetInvocationList())
        {
            if (h(msg, wParam, lParam))
            {
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
