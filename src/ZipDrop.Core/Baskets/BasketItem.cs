using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZipDrop.Core.Baskets;

public enum BasketItemKind
{
    File,
    Folder,
}

/// <summary>
/// A reference to a file or folder on disk. ZipDrop never copies the content
/// while collecting; it only remembers the original path.
/// </summary>
public sealed class BasketItem : INotifyPropertyChanged
{
    private bool _isMissing;
    private long? _sizeBytes;

    internal BasketItem(string fullPath, BasketItemKind kind)
    {
        FullPath = fullPath;
        Kind = kind;
        DisplayName = PathNames.GetDisplayName(fullPath);
    }

    public string FullPath { get; }
    public BasketItemKind Kind { get; }
    public string DisplayName { get; }
    public string? ParentDirectory => Path.GetDirectoryName(FullPath);

    /// <summary>True when the path no longer exists (moved/deleted after being added).</summary>
    public bool IsMissing
    {
        get => _isMissing;
        internal set => Set(ref _isMissing, value);
    }

    /// <summary>Size in bytes; null while a folder is still being measured.</summary>
    public long? SizeBytes
    {
        get => _sizeBytes;
        internal set => Set(ref _sizeBytes, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public override string ToString() => FullPath;
}
