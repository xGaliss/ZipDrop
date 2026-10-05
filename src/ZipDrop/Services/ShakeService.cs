using ZipDrop.Core.Gestures;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.Services;

/// <summary>
/// Glue between the global mouse hook and the pure <see cref="ShakeDetector"/>.
///
/// Windows offers no API to ask "is an Explorer file drag in progress?", so a drag is
/// inferred: a mouse button is held and the cursor moved more than
/// <see cref="ShakeOptions.DragStartDistance"/>. To avoid triggering while selecting text,
/// moving windows, drawing, etc., the button-down must happen over a shell file view
/// (File Explorer, desktop, file dialogs) — see <see cref="ShellSourceClasses"/>.
/// The hotkey remains the universal fallback for any other drag source.
///
/// Events are raised on the hook thread; subscribers must marshal to the UI thread.
/// </summary>
internal sealed class ShakeService : IDisposable
{
    /// <summary>
    /// Window classes (the window under the cursor or any ancestor) that count as a
    /// "file drag source". SHELLDLL_DefView covers the item view inside Explorer windows,
    /// the desktop and common Open/Save dialogs.
    /// </summary>
    public static readonly HashSet<string> ShellSourceClasses = new(StringComparer.Ordinal)
    {
        "CabinetWClass",    // File Explorer window (incl. navigation pane)
        "ExploreWClass",    // legacy Explorer
        "SHELLDLL_DefView", // shell item view (Explorer, dialogs, desktop)
        "Progman",          // desktop
        "WorkerW",          // desktop (when wallpaper slideshow/peek is active)
    };

    /// <summary>
    /// Only shell file views can start a shake. For development, set the environment
    /// variable ZIPDROP_SHAKE_ANY_SOURCE=1 to allow drags from any app.
    /// </summary>
    public static bool RequireShellSource { get; set; } =
        Environment.GetEnvironmentVariable("ZIPDROP_SHAKE_ANY_SOURCE") != "1";

    private readonly ShakeDetector _detector = new();
    private readonly MouseHook _hook;

    // Hook-thread state.
    private bool _buttonDown;
    private bool _dragging;
    private bool _firedThisDrag;
    private int _downX, _downY;
    private volatile int _sourceAllowed; // -1 unknown, 0 no, 1 yes
    private double _scale = 1.0;
    private int _moves;

    public ShakeService() => _hook = new MouseHook(OnMouse);

    /// <summary>Physical cursor position where the shake was recognized.</summary>
    public event Action<int, int>? ShakeDetected;

    /// <summary>A drag (button held + moved) ended. Arg: whether a shake fired during it.</summary>
    public event Action<bool>? DragEnded;

    public bool IsEnabled => _hook.IsRunning;

    public void Configure(bool enabled, ShakeSensitivity sensitivity)
    {
        _detector.Options = ShakeOptions.ForSensitivity(sensitivity);
        if (enabled) _hook.Start();
        else _hook.Stop();
    }

    public void Dispose() => _hook.Dispose();

    private void OnMouse(MouseEvent e)
    {
        switch (e.Kind)
        {
            case MouseEventKind.ButtonDown:
                _buttonDown = true;
                _dragging = false;
                _firedThisDrag = false;
                _downX = e.X;
                _downY = e.Y;
                _sourceAllowed = -1;
                _detector.Reset();
                var pt = new POINT(e.X, e.Y);
                // Window inspection off the hook thread: the hook callback must stay trivial.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    _scale = ScaleAt(pt);
                    _sourceAllowed = IsShellSource(pt) ? 1 : 0;
                    DevLog.Write($"ButtonDown at {pt.X},{pt.Y} scale={_scale} shellSource={_sourceAllowed}");
                });
                _moves = 0;
                break;

            case MouseEventKind.Move when _buttonDown:
                _moves++;
                var scale = _scale <= 0 ? 1.0 : _scale;
                if (!_dragging)
                {
                    var dx = (e.X - _downX) / scale;
                    var dy = (e.Y - _downY) / scale;
                    if (dx * dx + dy * dy < _detector.Options.DragStartDistance * _detector.Options.DragStartDistance) return;
                    _dragging = true;
                }

                if (_firedThisDrag) return; // one summon per drag is enough
                if (!_detector.Feed(e.X / scale, e.Y / scale, e.TimeMs)) return;
                DevLog.Write($"Shake recognized at {e.X},{e.Y} source={_sourceAllowed}");
                if (RequireShellSource && _sourceAllowed != 1) return;

                _firedThisDrag = true;
                ShakeDetected?.Invoke(e.X, e.Y);
                break;

            case MouseEventKind.ButtonUp:
                var wasDragging = _dragging;
                var fired = _firedThisDrag;
                _buttonDown = _dragging = _firedThisDrag = false;
                _detector.Reset();
                DevLog.Write($"ButtonUp dragging={wasDragging} moves={_moves} shakeFired={fired}");
                if (wasDragging) DragEnded?.Invoke(fired);
                break;
        }
    }

    private static bool IsShellSource(POINT pt)
    {
        var hwnd = WindowFromPoint(pt);
        for (var depth = 0; hwnd != IntPtr.Zero && depth < 32; depth++)
        {
            if (ShellSourceClasses.Contains(ClassNameOf(hwnd))) return true;
            hwnd = GetParent(hwnd);
        }
        return false;
    }
}
