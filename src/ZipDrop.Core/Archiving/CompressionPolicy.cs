using System.IO.Compression;

namespace ZipDrop.Core.Archiving;

/// <summary>
/// Picks a compression level per file. Already-compressed formats are stored
/// (NoCompression): deflating them again costs a lot of CPU for ~0% gain, which
/// matters most for the large photos/videos people typically zip.
/// User-selectable levels are a roadmap item, not MVP.
/// </summary>
public static class CompressionPolicy
{
    private static readonly HashSet<string> AlreadyCompressed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".avif",
        ".mp3", ".aac", ".m4a", ".ogg", ".opus", ".flac",
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv",
        ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".epub",
        ".jar", ".apk", ".msi", ".nupkg", ".vsix", ".appx", ".msix",
    };

    public static CompressionLevel ForFile(string name) =>
        AlreadyCompressed.Contains(Path.GetExtension(name)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
}
