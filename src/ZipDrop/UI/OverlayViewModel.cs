using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using ZipDrop.Core.Archiving;
using ZipDrop.Core.Baskets;
using ZipDrop.Core.Formatting;
using ZipDrop.Core.Settings;

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

    private OverlayMode _mode;
    private bool _isDragOver;
    private bool _isListExpanded;
    private bool _clearArmed;
    private string? _toast;
    private double _progress;
    private string _progressText = "";
    private string _resultTitle = "";
    private string _resultDetail = "";
    private string? _resultPath;
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
        DismissCommand = new RelayCommand(Dismiss);
        ToggleListCommand = new RelayCommand(() => IsListExpanded = !IsListExpanded);
        RemoveItemCommand = new RelayCommand(p => { if (p is BasketItem i) Basket.Remove(i); });
        RemoveMissingCommand = new RelayCommand(() => ShowToast($"Removed {Basket.RemoveMissing()} missing"));
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
    }

    public Basket Basket { get; }

    /// <summary>Set by the view: shows the native Save dialog and returns the chosen path or null.</summary>
    public Func<string, string, string?>? PickDestination { get; set; }

    public event Action? CloseRequested;
    public event Action? SettingsRequested;
    /// <summary>Raised after a ZIP finished (successfully or not). Arg: message for a tray notification.</summary>
    public event Action<string, bool>? ZipFinished;

    public ICommand CreateZipCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand ToggleListCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand RemoveMissingCommand { get; }
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

    public string ClearText => ClearArmed ? "Click again to clear" : "Clear";

    public string? Toast
    {
        get => _toast;
        private set { if (Set(ref _toast, value)) Raise(nameof(HasToast)); }
    }

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    public string CountText => SizeFormatter.Items(Basket.Count);

    public string SizeText => Basket.IsMeasuring
        ? $"{SizeFormatter.Format(Basket.TotalBytes)}…"
        : SizeFormatter.Format(Basket.TotalBytes);

    /// <summary>Icons of the last (up to 3) items added, back to front, for the fanned preview stack.</summary>
    private IReadOnlyList<System.Windows.Media.ImageSource> PreviewIcons =>
        Basket.Items.Reverse().Take(3).Reverse()
            .Select(i => Services.ShellIcons.For(i, Services.ShellIcons.Size.ExtraLarge))
            .OfType<System.Windows.Media.ImageSource>()
            .ToList();

    public System.Windows.Media.ImageSource? PreviewIcon1 => PreviewAt(0);
    public System.Windows.Media.ImageSource? PreviewIcon2 => PreviewAt(1);
    public System.Windows.Media.ImageSource? PreviewIcon3 => PreviewAt(2);

    private System.Windows.Media.ImageSource? PreviewAt(int index)
    {
        // Slot 3 is the front (most recent); with fewer items the back slots stay empty.
        var icons = PreviewIcons;
        var slot = index - (3 - icons.Count);
        return slot >= 0 ? icons[slot] : null;
    }

    public bool HasMissing => Basket.MissingCount > 0;
    public string MissingText => $"{Basket.MissingCount} missing";

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string ResultTitle { get => _resultTitle; private set => Set(ref _resultTitle, value); }
    public string ResultDetail { get => _resultDetail; private set => Set(ref _resultDetail, value); }
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }

    // ---------- Actions ----------

    /// <summary>Adds dropped paths and gives short feedback.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        if (Mode is OverlayMode.Done or OverlayMode.Error) Mode = OverlayMode.Collect;

        var result = Basket.Add(paths);
        Services.DevLog.Write($"AddPaths added={result.Added.Count} dup={result.Duplicates.Count} notFound={string.Join("|", result.NotFound)}");
        var parts = new List<string>();
        if (result.Added.Count > 0) parts.Add($"+{result.Added.Count} added");
        if (result.Duplicates.Count > 0) parts.Add($"{result.Duplicates.Count} already in basket");
        if (result.NotFound.Count > 0) parts.Add($"{result.NotFound.Count} not found");
        if (parts.Count > 0) ShowToast(string.Join(" · ", parts));

        if (result.Added.Count > 0) _ = MeasureAsync();
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

    private void Dismiss()
    {
        Mode = OverlayMode.Collect;
    }

    private void OpenFolder()
    {
        if (_resultPath is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_resultPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowToast("Could not open the folder");
        }
    }

    private async Task MeasureAsync()
    {
        try { await Basket.MeasurePendingAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
    }

    private async Task CreateZipAsync()
    {
        if (IsBusy) return;
        Refresh();

        var initialDir = _lastDirectory is not null && Directory.Exists(_lastDirectory)
            ? _lastDirectory
            : Basket.Items.FirstOrDefault(i => !i.IsMissing)?.ParentDirectory
              ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var destination = PickDestination?.Invoke("Archive.zip", initialDir);
        if (destination is null) return;
        _lastDirectory = Path.GetDirectoryName(destination);

        // Missing items are passed too: the planner skips them and reports them in the result.
        var sources = Basket.Items.Select(i => new ZipSource(i.FullPath, i.Kind)).ToList();
        _zipCts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = "Preparing…";
        IsListExpanded = false;
        Mode = OverlayMode.Zipping;

        var progress = new Progress<ZipProgress>(p =>
        {
            Progress = p.Fraction;
            ProgressText = p.BytesTotal > 0
                ? $"{p.Fraction:P0} · {SizeFormatter.Format(p.BytesDone)} of {SizeFormatter.Format(p.BytesTotal)}"
                : $"{p.FilesDone} of {p.FilesTotal} files";
        });

        try
        {
            var result = await ZipBuilder.CreateAsync(sources, destination, progress, _zipCts.Token);
            _resultPath = result.DestinationPath;
            ResultTitle = "ZIP created";
            ResultDetail = Describe(result);
            Mode = OverlayMode.Done;

            if (_settings().ClearBasketAfterZip && result.Skipped.Count == 0)
            {
                Basket.Clear();
                IsListExpanded = false;
            }
            ZipFinished?.Invoke($"{Path.GetFileName(result.DestinationPath)} is ready.", false);
        }
        catch (OperationCanceledException)
        {
            Mode = OverlayMode.Collect;
            ShowToast("ZIP cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            ErrorText = ex.Message;
            Mode = OverlayMode.Error;
            ZipFinished?.Invoke($"Could not create the ZIP: {ex.Message}", true);
        }
        finally
        {
            _zipCts.Dispose();
            _zipCts = null;
        }
    }

    private static string Describe(ZipResult r)
    {
        var parts = new List<string> { $"{Path.GetFileName(r.DestinationPath)} · {SizeFormatter.Format(SafeLength(r.DestinationPath))}" };
        if (r.Skipped.Count > 0) parts.Add($"{r.Skipped.Count} file(s) skipped (in use or no access)");
        if (r.MissingSources.Count > 0) parts.Add($"{r.MissingSources.Count} missing item(s) not included");
        if (r.Renames.Count > 0) parts.Add($"{r.Renames.Count} renamed to avoid name conflicts");
        return string.Join("\n", parts);
    }

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
