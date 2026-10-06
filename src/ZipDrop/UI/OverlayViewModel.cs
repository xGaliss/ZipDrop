using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZipDrop.Core.Archiving;
using ZipDrop.Core.Baskets;
using ZipDrop.Core.Formatting;
using ZipDrop.Core.Settings;
using ZipDrop.Services;

namespace ZipDrop.UI;

internal enum OverlayMode
{
    Collect,
    Zipping,
    Done,
    Error,
}

/// <summary>State and commands of the floating basket overlay.</summary>
internal sealed class OverlayViewModel : Observable
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(2.6);
    private static readonly TimeSpan ClearConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _clearArmTimer;
    private CancellationTokenSource? _zipCts;
    private string? _lastDirectory;
    private bool _picking;

    private OverlayMode _mode;
    private bool _isDragOver;
    private bool _isListExpanded;
    private bool _clearArmed;
    private string? _toast;
    private double _progress;
    private string _progressText = "";
    private string? _resultPath;
    private string _resultSizeText = "";
    private string _resultNotes = "";
    private string _errorText = "";

    public OverlayViewModel(Basket basket, Func<AppSettings> settings)
    {
        Basket = basket;
        _settings = settings;
        Basket.PropertyChanged += OnBasketChanged;

        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };
        _clearArmTimer = new DispatcherTimer { Interval = ClearConfirmWindow };
        _clearArmTimer.Tick += (_, _) => { _clearArmTimer.Stop(); ClearArmed = false; };

        CreateZipCommand = new RelayCommand(() => _ = CreateZipAsync(), () => Mode == OverlayMode.Collect && Basket.Count > Basket.MissingCount);
        ClearCommand = new RelayCommand(Clear, () => Mode == OverlayMode.Collect && !Basket.IsEmpty);
        CancelCommand = new RelayCommand(() => _zipCts?.Cancel());
        OpenFolderCommand = new RelayCommand(OpenFolder);
        CopyZipCommand = new RelayCommand(CopyZip, () => ResultPath is not null);
        DismissCommand = new RelayCommand(Dismiss);
        ToggleListCommand = new RelayCommand(() => IsListExpanded = !IsListExpanded);
        RemoveItemCommand = new RelayCommand(p => { if (p is BasketItem i) Basket.Remove(i); });
        RemoveMissingCommand = new RelayCommand(() => ShowToast(Strings.RemovedMissing(Basket.RemoveMissing())));
        PasteCommand = new RelayCommand(Paste, () => !IsBusy);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
    }

    public Basket Basket { get; }

    /// <summary>Set by the view: shows the native Save dialog and returns the chosen path or null.</summary>
    public Func<string, string, string?>? PickDestination { get; set; }

    public event Action? CloseRequested;
    public event Action? SettingsRequested;
    /// <summary>Raised after a ZIP finished (successfully or not). Args: message for a tray notification, isError.</summary>
    public event Action<string, bool>? ZipFinished;

    public ICommand CreateZipCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyZipCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand ToggleListCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand RemoveMissingCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SettingsCommand { get; }

    // ---------- State ----------

    public OverlayMode Mode
    {
        get => _mode;
        private set
        {
            if (!Set(ref _mode, value)) return;
            RaiseAll(nameof(IsCollect), nameof(IsZipping), nameof(IsDone), nameof(IsError), nameof(ShowEmpty), nameof(ShowSummary), nameof(ShowList));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsCollect => Mode == OverlayMode.Collect;
    public bool IsZipping => Mode == OverlayMode.Zipping;
    public bool IsDone => Mode == OverlayMode.Done;
    public bool IsError => Mode == OverlayMode.Error;
    public bool IsBusy => Mode == OverlayMode.Zipping;

    public bool ShowEmpty => IsCollect && Basket.IsEmpty;
    public bool ShowSummary => IsCollect && !Basket.IsEmpty;
    public bool ShowList => ShowSummary && IsListExpanded;

    public bool IsDragOver
    {
        get => _isDragOver;
        set => Set(ref _isDragOver, value);
    }

    public bool IsListExpanded
    {
        get => _isListExpanded;
        set { if (Set(ref _isListExpanded, value)) Raise(nameof(ShowList)); }
    }

    public bool ClearArmed
    {
        get => _clearArmed;
        private set { if (Set(ref _clearArmed, value)) Raise(nameof(ClearText)); }
    }

    public string ClearText => ClearArmed ? Strings.ClickAgainToClear : Strings.Clear;

    public string? Toast
    {
        get => _toast;
        private set { if (Set(ref _toast, value)) Raise(nameof(HasToast)); }
    }

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    public string CountText => Strings.ItemCount(Basket.Count);

    public string SizeText => Basket.IsMeasuring ? $"{Size(Basket.TotalBytes)}…" : Size(Basket.TotalBytes);

    /// <summary>Icons of the last (up to 3) items added, back to front, for the fanned preview stack.</summary>
    private IReadOnlyList<ImageSource> PreviewIcons =>
        Basket.Items.Reverse().Take(3).Reverse()
            .Select(i => ShellIcons.For(i, ShellIcons.Size.ExtraLarge))
            .OfType<ImageSource>()
            .ToList();

    public ImageSource? PreviewIcon1 => PreviewAt(0);
    public ImageSource? PreviewIcon2 => PreviewAt(1);
    public ImageSource? PreviewIcon3 => PreviewAt(2);

    private ImageSource? PreviewAt(int index)
    {
        // Slot 3 is the front (most recent); with fewer items the back slots stay empty.
        var icons = PreviewIcons;
        var slot = index - (3 - icons.Count);
        return slot >= 0 ? icons[slot] : null;
    }

    public bool HasMissing => Basket.MissingCount > 0;
    public string MissingText => Strings.MissingCount(Basket.MissingCount);

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }

    /// <summary>Full path of the last ZIP created (for drag-out, copy, open folder).</summary>
    public string? ResultPath
    {
        get => _resultPath;
        private set
        {
            if (!Set(ref _resultPath, value)) return;
            RaiseAll(nameof(ResultName), nameof(ResultIcon));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ResultName => ResultPath is null ? "" : Path.GetFileName(ResultPath);
    public ImageSource? ResultIcon => ShellIcons.For(".zip", ShellIcons.Size.ExtraLarge);
    public string ResultSizeText { get => _resultSizeText; private set => Set(ref _resultSizeText, value); }

    /// <summary>Skipped/missing/renamed notes; empty when everything went fine.</summary>
    public string ResultNotes
    {
        get => _resultNotes;
        private set { if (Set(ref _resultNotes, value)) Raise(nameof(HasResultNotes)); }
    }

    public bool HasResultNotes => !string.IsNullOrEmpty(ResultNotes);

    // ---------- Actions ----------

    /// <summary>Adds dropped/pasted paths and gives short feedback.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        if (Mode is OverlayMode.Done or OverlayMode.Error) Mode = OverlayMode.Collect;

        var result = Basket.Add(paths);
        DevLog.Write($"AddPaths added={result.Added.Count} dup={result.Duplicates.Count} notFound={string.Join("|", result.NotFound)}");
        var parts = new List<string>();
        if (result.Added.Count > 0) parts.Add(Strings.Added(result.Added.Count));
        if (result.Duplicates.Count > 0) parts.Add(Strings.AlreadyInBasket(result.Duplicates.Count));
        if (result.NotFound.Count > 0) parts.Add(Strings.NotFound(result.NotFound.Count));
        if (parts.Count > 0) ShowToast(string.Join(" · ", parts));

        if (result.Added.Count > 0) _ = MeasureAsync();
    }

    /// <summary>Ctrl+V: adds files copied (or cut) in Explorer. Originals are never moved.</summary>
    public void Paste()
    {
        if (IsBusy) return;
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                ShowToast(Strings.NothingToPaste);
                return;
            }
            AddPaths(Clipboard.GetFileDropList().Cast<string>().ToList());
        }
        catch (Exception ex) when (ex is ExternalException)
        {
            ShowToast(Strings.ClipboardBusy);
        }
    }

    /// <summary>Re-checks missing files. Called periodically while the overlay is visible.</summary>
    public void Refresh()
    {
        if (Basket.RefreshExistence()) CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>When the overlay hides, finished/error panels go back to the basket view.</summary>
    public void OnHidden()
    {
        if (Mode is OverlayMode.Done or OverlayMode.Error) Mode = OverlayMode.Collect;
        ClearArmed = false;
    }

    /// <summary>"New basket" from the tray. Single basket for now (multiple baskets is a roadmap item).</summary>
    public void NewBasket()
    {
        if (IsBusy) return;
        Basket.Clear();
        IsListExpanded = false;
        Mode = OverlayMode.Collect;
    }

    public void CancelZip() => _zipCts?.Cancel();

    private void Clear()
    {
        if (!ClearArmed)
        {
            ClearArmed = true;
            _clearArmTimer.Stop();
            _clearArmTimer.Start();
            return;
        }
        _clearArmTimer.Stop();
        ClearArmed = false;
        Basket.Clear();
        IsListExpanded = false;
    }

    private void Dismiss() => Mode = OverlayMode.Collect;

    private void OpenFolder()
    {
        if (ResultPath is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ResultPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast(Strings.CouldNotOpenFolder);
        }
    }

    /// <summary>Puts the ZIP on the clipboard as a file (like Ctrl+C in Explorer).</summary>
    private void CopyZip()
    {
        if (ResultPath is null || !File.Exists(ResultPath)) return;
        try
        {
            Clipboard.SetDataObject(FileDataObject.Create(ResultPath), copy: true);
            ShowToast(Strings.CopiedZip);
        }
        catch (Exception ex) when (ex is ExternalException)
        {
            ShowToast(Strings.ClipboardBusy);
        }
    }

    private async Task MeasureAsync()
    {
        try { await Basket.MeasurePendingAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
    }

    private async Task CreateZipAsync()
    {
        // The Save dialog is modal, but automation (or a second click racing it) can still re-enter.
        if (IsBusy || _picking) return;
        Refresh();

        var initialDir = _lastDirectory is not null && Directory.Exists(_lastDirectory)
            ? _lastDirectory
            : Basket.Items.FirstOrDefault(i => !i.IsMissing)?.ParentDirectory
              ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        string? destination;
        _picking = true;
        try { destination = PickDestination?.Invoke(Strings.SuggestedName, initialDir); }
        finally { _picking = false; }
        if (destination is null) return;
        _lastDirectory = Path.GetDirectoryName(destination);

        // Missing items are passed too: the planner skips them and reports them in the result.
        var sources = Basket.Items.Select(i => new ZipSource(i.FullPath, i.Kind)).ToList();
        _zipCts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = Strings.Preparing;
        IsListExpanded = false;
        Mode = OverlayMode.Zipping;

        var progress = new Progress<ZipProgress>(p =>
        {
            Progress = p.Fraction;
            ProgressText = p.BytesTotal > 0
                ? Strings.ProgressBytes(p.Fraction, Size(p.BytesDone), Size(p.BytesTotal))
                : Strings.ProgressFiles(p.FilesDone, p.FilesTotal);
        });

        try
        {
            var result = await ZipBuilder.CreateAsync(sources, destination, progress, _zipCts.Token);
            ResultPath = result.DestinationPath;
            ResultSizeText = Size(SafeLength(result.DestinationPath));
            ResultNotes = Notes(result);
            Mode = OverlayMode.Done;

            if (_settings().ClearBasketAfterZip && result.Skipped.Count == 0)
            {
                Basket.Clear();
                IsListExpanded = false;
            }
            ZipFinished?.Invoke(Strings.ZipReady(ResultName), false);
        }
        catch (OperationCanceledException)
        {
            Mode = OverlayMode.Collect;
            ShowToast(Strings.ZipCancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            ErrorText = ex is NothingToZipException ? Strings.NothingToZip : ex.Message;
            Mode = OverlayMode.Error;
            ZipFinished?.Invoke(Strings.ZipFailed(ErrorText), true);
        }
        finally
        {
            _zipCts.Dispose();
            _zipCts = null;
        }
    }

    private static string Notes(ZipResult r)
    {
        var parts = new List<string>();
        if (r.Skipped.Count > 0) parts.Add(Strings.SkippedFiles(r.Skipped.Count));
        if (r.MissingSources.Count > 0) parts.Add(Strings.MissingNotIncluded(r.MissingSources.Count));
        if (r.Renames.Count > 0) parts.Add(Strings.Renamed(r.Renames.Count));
        return string.Join("\n", parts);
    }

    private static string Size(long bytes) => SizeFormatter.Format(bytes, Strings.Culture);

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private void ShowToast(string text)
    {
        Toast = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnBasketChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseAll(nameof(CountText), nameof(SizeText), nameof(HasMissing), nameof(MissingText),
            nameof(ShowEmpty), nameof(ShowSummary), nameof(ShowList));
        if (e.PropertyName == nameof(Basket.Count))
            RaiseAll(nameof(PreviewIcon1), nameof(PreviewIcon2), nameof(PreviewIcon3));
        if (Basket.IsEmpty) IsListExpanded = false;
        CommandManager.InvalidateRequerySuggested();
    }
}
