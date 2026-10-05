using System.Runtime.InteropServices;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

internal enum MouseEventKind
{
    Move,
    ButtonDown,
    ButtonUp,
}

internal readonly record struct MouseEvent(MouseEventKind Kind, int X, int Y, long TimeMs);

/// <summary>
/// Global low-level mouse hook (WH_MOUSE_LL) on a dedicated thread with its own
/// message loop. LL hooks see input even while another process runs an OLE
/// drag loop (DoDragDrop), which is exactly when we need it.
///
/// The callback must return fast (Windows silently removes LL hooks that exceed
/// LowLevelHooksTimeout), so the handler should do only trivial work.
/// Coordinates are physical pixels (the process is Per-Monitor-V2 DPI aware).
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly Action<MouseEvent> _handler;
    private Thread? _thread;
    private uint _threadId;
    private LowLevelMouseProc? _proc; // keep the delegate alive while hooked
    private readonly ManualResetEventSlim _started = new();

    public MouseHook(Action<MouseEvent> handler) => _handler = handler;

    public bool IsRunning => _thread is not null;
    public int LastError { get; private set; }

    public bool Start()
    {
        if (_thread is not null) return true;
        _started.Reset();
        var hookOk = false;
        _thread = new Thread(() => Run(ok => { hookOk = ok; _started.Set(); }))
        {
            IsBackground = true,
            Name = "ZipDrop mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(2));
        if (!hookOk) Stop();
        return hookOk;
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread is null) return;
        _thread = null;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(1));
        _threadId = 0;
    }

    public void Dispose() => Stop();

    private void Run(Action<bool> signal)
    {
        _threadId = GetCurrentThreadId();
        _proc = HookProc;
        var hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
        {
            LastError = Marshal.GetLastWin32Error();
            signal(false);
            return;
        }
        signal(true);

        try
        {
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
                // No windows on this thread; the loop only exists so the hook gets called.
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            MouseEventKind? kind = msg switch
            {
                WM_MOUSEMOVE => MouseEventKind.Move,
                WM_LBUTTONDOWN or WM_RBUTTONDOWN => MouseEventKind.ButtonDown,
                WM_LBUTTONUP or WM_RBUTTONUP => MouseEventKind.ButtonUp,
                _ => null,
            };
            if (kind is { } k)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                try { _handler(new MouseEvent(k, data.pt.X, data.pt.Y, data.time)); }
                catch { /* never let an exception escape into the hook chain */ }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}
