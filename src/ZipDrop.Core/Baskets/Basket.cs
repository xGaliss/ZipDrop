using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZipDrop.Core.FileSystem;

namespace ZipDrop.Core.Baskets;

public sealed record BasketAddResult(
    IReadOnlyList<BasketItem> Added,
    IReadOnlyList<string> Duplicates,
    IReadOnlyList<string> NotFound);

/// <summary>
/// A temporary collection of path references that will become one ZIP.
/// Not thread-safe: owned by the UI thread. Background work (measuring)
/// is awaited so continuations come back to the caller's context.
/// </summary>
/// <remarks>
/// Designed so several baskets can coexist in the future (multiple baskets roadmap item):
/// nothing here is static and a basket has its own name.
/// </remarks>
public sealed class Basket : INotifyPropertyChanged
{
    private readonly ObservableCollection<BasketItem> _items = new();
    private readonly HashSet<string> _paths = new(PathNames.Comparer);

    public Basket(string name = "Archive")
    {
        Name = name;
        Items = new ReadOnlyObservableCollection<BasketItem>(_items);
    }

    public string Name { get; }
    public ReadOnlyObservableCollection<BasketItem> Items { get; }

    public int Count => _items.Count;
    public int MissingCount => _items.Count(i => i.IsMissing);
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Sum of known sizes of non-missing items.</summary>
    public long TotalBytes => _items.Where(i => !i.IsMissing).Sum(i => i.SizeBytes ?? 0);

    /// <summary>True while at least one folder is still being measured.</summary>
    public bool IsMeasuring => _items.Any(i => i.SizeBytes is null && !i.IsMissing);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Adds paths. Exact same path (case-insensitive, normalized) is never added twice.
    /// Files get their size immediately; folders are measured by <see cref="MeasurePendingAsync"/>.
    /// </summary>
    public BasketAddResult Add(IEnumerable<string> paths)
    {
        var added = new List<BasketItem>();
        var duplicates = new List<string>();
        var notFound = new List<string>();

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string full;
            try { full = PathNames.Normalize(raw); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                notFound.Add(raw);
                continue;
            }

            if (_paths.Contains(full))
            {
                duplicates.Add(full);
                continue;
            }

            BasketItem item;
            if (File.Exists(full))
            {
                item = new BasketItem(full, BasketItemKind.File) { SizeBytes = SafeFileLength(full) };
            }
            else if (Directory.Exists(full))
            {
                item = new BasketItem(full, BasketItemKind.Folder);
            }
            else
            {
                notFound.Add(full);
                continue;
            }

            item.PropertyChanged += OnItemChanged;
            _paths.Add(full);
            _items.Add(item);
            added.Add(item);
        }

        if (added.Count > 0) RaiseAggregates();
        return new BasketAddResult(added, duplicates, notFound);
    }

    public bool Remove(BasketItem item)
    {
        if (!_items.Remove(item)) return false;
        item.PropertyChanged -= OnItemChanged;
        _paths.Remove(item.FullPath);
        RaiseAggregates();
        return true;
    }

    public void Clear()
    {
        foreach (var item in _items) item.PropertyChanged -= OnItemChanged;
        _items.Clear();
        _paths.Clear();
        RaiseAggregates();
    }

    /// <summary>Removes every item currently flagged as missing.</summary>
    public int RemoveMissing()
    {
        var missing = _items.Where(i => i.IsMissing).ToList();
        foreach (var item in missing) Remove(item);
        return missing.Count;
    }

    /// <summary>
    /// Re-checks that every referenced path still exists with the same kind.
    /// Items that come back (e.g. undo of a delete) are un-flagged.
    /// Returns true if any flag changed.
    /// </summary>
    public bool RefreshExistence()
    {
        var changed = false;
        foreach (var item in _items)
        {
            var exists = item.Kind == BasketItemKind.File ? File.Exists(item.FullPath) : Directory.Exists(item.FullPath);
            if (item.IsMissing == !exists) continue;
            item.IsMissing = !exists;
            changed = true;
            if (exists && item.Kind == BasketItemKind.File) item.SizeBytes = SafeFileLength(item.FullPath);
        }
        return changed;
    }

    /// <summary>Measures every folder whose size is unknown, on a background thread.</summary>
    public async Task MeasurePendingAsync(CancellationToken ct = default)
    {
        var pending = _items.Where(i => i.Kind == BasketItemKind.Folder && i.SizeBytes is null).ToList();
        foreach (var item in pending)
        {
            var path = item.FullPath;
            var size = await Task.Run(() => FolderScanner.MeasureBytes(path, ct), ct);
            if (_items.Contains(item)) item.SizeBytes = size;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => RaiseAggregates();

    private void RaiseAggregates()
    {
        Raise(nameof(Count));
        Raise(nameof(MissingCount));
        Raise(nameof(IsEmpty));
        Raise(nameof(TotalBytes));
        Raise(nameof(IsMeasuring));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
