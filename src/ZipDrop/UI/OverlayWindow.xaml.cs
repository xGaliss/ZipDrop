using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using ZipDrop.Services;
using static ZipDrop.Interop.NativeMethods;

namespace ZipDrop.UI;

/// <summary>
/// The floating basket. Never activates itself (so the user's current app keeps focus),
/// is topmost while visible and is hidden — not closed — when dismissed.
/// </summary>
internal partial class OverlayWindow : Window
{
    /// <summary>When summoned by a shake and the drag ends without a drop, hide after this delay.</summary>
    private static readonly TimeSpan NoDropHideDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>When summoned by a shake and something was dropped, tuck away after this idle time.</summary>
    private static readonly TimeSpan AfterDropHideDelay = TimeSpan.FromSeconds(3.5);

    private static readonly TimeSpan MissingCheckInterval = TimeSpan.FromSeconds(2);

    private readonly OverlayViewModel _vm;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private IntPtr _hwnd;
    private bool _summonedByShake;
    private bool _droppedSinceSummon;
    private bool _hiding;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PickDestination = PickDestination;
        vm.PropertyChanged += OnViewModelChanged;

        _refreshTimer = new DispatcherTimer { Interval = MissingCheckInterval };
        _refreshTimer.Tick += (_, _) => _vm.Refresh();

        _autoHideTimer = new DispatcherTimer();
        _autoHideTimer.Tick += OnAutoHideTick;

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _vm.Refresh(); _refreshTimer.Start(); }
            else { _refreshTimer.Stop(); _vm.OnHidden(); }
        };
        SizeChanged += (_, _) => EnsureOnScreen();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(EnsureOnScreen, DispatcherPriority.Loaded);
        MouseEnter += (_, _) => _autoHideTimer.Stop();
        MouseLeave += (_, _) => { if (_summonedByShake && _droppedSinceSummon) StartAutoHide(AfterDropHideDelay); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HideAnimated(); };

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Tool window: no Alt+Tab entry, no taskbar button.
        var ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
    }

    // ---------- Show / hide ----------

    /// <summary>Shows next to a physical screen point (cursor) without stealing focus.</summary>
    public void ShowNear(int x, int y)
    {
        _autoHideTimer.Stop();
        _summonedByShake = false;
        var wasVisible = IsVisible && !_hiding;
        if (!wasVisible) PrepareShow();
        UpdateLayout();
        PlaceNear(x, y);
        if (!wasVisible) AnimateIn();
    }

    public void ShowNearCursor()
    {
        GetCursorPos(out var p);
        ShowNear(p.X, p.Y);
    }

    public void Toggle()
    {
        if (IsVisible && !_hiding) HideAnimated();
        else ShowNearCursor();
    }

    /// <summary>Called when a shake was recognized during a drag.</summary>
    public void SummonForDrag(int x, int y)
    {
        var wasVisible = IsVisible && !_hiding;
        if (wasVisible && IsOnSameMonitor(x, y))
        {
            _autoHideTimer.Stop();
            return; // already there: don't make it jump under the user's cursor
        }
        ShowNear(x, y);
        _summonedByShake = !wasVisible;
        _droppedSinceSummon = false;
    }

    /// <summary>The global drag that summoned us ended (button released somewhere).</summary>
    public void OnGlobalDragEnded()
    {
        if (!_summonedByShake) return;
        // The hook sees the button-up before Explorer delivers Drop to us, so decide a bit later.
        if (!_droppedSinceSummon) StartAutoHide(NoDropHideDelay);
    }

    public void HideAnimated()
    {
        if (!IsVisible || _hiding) return;
        _autoHideTimer.Stop();
        _summonedByShake = false;
        _hiding = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) =>
        {
            if (!_hiding) return; // re-shown during the fade
            _hiding = false;
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void PrepareShow()
    {
        _hiding = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();
    }

    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        var scale = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void StartAutoHide(TimeSpan delay)
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Interval = delay;
        _autoHideTimer.Start();
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        if (!_summonedByShake) return;
        if (IsMouseOver || _vm.IsBusy || _vm.IsDragOver) return; // MouseLeave restarts it
        HideAnimated();
    }

    // ---------- Placement (physical pixels, per-monitor DPI) ----------

    private void PlaceNear(int px, int py)
    {
        if (_hwnd == IntPtr.Zero) return;
        var pt = new POINT(px, py);
        var scale = ScaleAt(pt);
        var work = WorkAreaAt(pt);
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var gap = (int)(6 * scale);

        var x = px + gap;
        if (x + w > work.Right) x = px - gap - w;
        var y = py - h / 2;

        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Keeps the card inside the work area when it grows (item list) or changes DPI.</summary>
    private void EnsureOnScreen()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || !GetWindowRect(_hwnd, out var r)) return;
        var center = new POINT(r.Left + r.Width / 2, r.Top + r.Height / 2);
        var work = WorkAreaAt(center);
        var x = Math.Clamp(r.Left, work.Left, Math.Max(work.Left, work.Right - r.Width));
        var y = Math.Clamp(r.Top, work.Top, Math.Max(work.Top, work.Bottom - r.Height));
        if (x != r.Left || y != r.Top)
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private bool IsOnSameMonitor(int x, int y) =>
        _hwnd != IntPtr.Zero &&
        MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST) == MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        _summonedByShake = false; // user is placing it deliberately
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    // ---------- Drag & drop ----------

    private static DragDropEffects ChooseEffect(DragEventArgs e)
    {
        // Never MOVE: with a move effect Explorer may delete the originals after the drop.
        if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        return DragDropEffects.None;
    }

    private bool Accepts(DragEventArgs e) => !_vm.IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var ok = Accepts(e);
        DevLog.Write($"DragEnter accepts={ok} allowed={e.AllowedEffects} formats={string.Join(",", e.Data.GetFormats())}");
        e.Effects = ok ? ChooseEffect(e) : DragDropEffects.None;
        e.Handled = true;
        ShellDragImage.Enter(this, _hwnd, e);
        _autoHideTimer.Stop();
        if (!ok) return;
        DropHint.Text = _vm.Basket.IsEmpty ? "Add to ZIP" : $"Add to {_vm.CountText}";
        _vm.IsDragOver = true;
        ((Storyboard)Resources["DropIn"]).Begin(this, true);
        ((Storyboard)Resources["DropBob"]).Begin(this, true);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Accepts(e) ? ChooseEffect(e) : DragDropEffects.None;
        e.Handled = true;
        ShellDragImage.Over(this, e);
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ShellDragImage.Leave();
        EndDragVisual();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var ok = Accepts(e);
        DevLog.Write($"Drop accepts={ok} data={e.Data.GetData(DataFormats.FileDrop)?.GetType().Name}");
        e.Effects = ok ? ChooseEffect(e) : DragDropEffects.None;
        e.Handled = true;
        ShellDragImage.Drop(this, e);
        EndDragVisual();
        if (!ok || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        _droppedSinceSummon = true;
        _vm.AddPaths(paths);
        Bump();
        if (_summonedByShake && !IsMouseOver) StartAutoHide(AfterDropHideDelay);
    }

    private void EndDragVisual()
    {
        if (!_vm.IsDragOver) return;
        _vm.IsDragOver = false;
        ((Storyboard)Resources["DropBob"]).Stop(this);
        ((Storyboard)Resources["DropOut"]).Begin(this, true);
    }

    /// <summary>Small "pop" on the item count after a drop.</summary>
    private void Bump()
    {
        var pop = new DoubleAnimation(1.12, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
        CountScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        CountScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // ---------- Misc ----------

    private string? PickDestination(string suggestedName, string initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save ZIP",
            FileName = suggestedName,
            DefaultExt = ".zip",
            AddExtension = true,
            Filter = "ZIP archive (*.zip)|*.zip",
            InitialDirectory = initialDirectory,
            OverwritePrompt = true,
        };
        _autoHideTimer.Stop();
        _summonedByShake = false;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsListExpanded))
            Chevron.Text = (string)FindResource(_vm.IsListExpanded ? "Glyph.ChevronUp" : "Glyph.ChevronDown");
    }
}
