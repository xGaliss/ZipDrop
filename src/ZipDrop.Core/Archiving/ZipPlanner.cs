using ZipDrop.Core.Baskets;
using ZipDrop.Core.FileSystem;

namespace ZipDrop.Core.Archiving;

public sealed record ZipSource(string FullPath, BasketItemKind Kind);

public sealed record ZipPlanEntry(
    string? SourcePath,
    string EntryName,
    bool IsDirectory,
    long Length,
    DateTime LastWriteTime);

public sealed record ZipRename(string SourcePath, string OriginalName, string NewName);

public sealed class ZipPlan
{
    public required IReadOnlyList<ZipPlanEntry> Entries { get; init; }
    public required IReadOnlyList<string> MissingSources { get; init; }
    public required IReadOnlyList<ZipRename> Renames { get; init; }
    public long TotalBytes => Entries.Sum(e => e.Length);
    public int FileCount => Entries.Count(e => !e.IsDirectory);
}

/// <summary>
/// Turns basket references into a flat list of ZIP entries.
///
/// Naming strategy (see docs/DECISIONS.md, D-006):
///  * Every basket item becomes a top-level entry named after the item
///    ("foto.jpg", "proyecto/...").
///  * Names are compared case-insensitively (Windows extraction is case-insensitive).
///  * When two items would get the same top-level name, the later one is renamed
///    "name (2).ext", "name (3).ext"... Nothing is ever overwritten.
///  * Folders keep their internal structure untouched.
///  * The destination ZIP (and its temp file) is never included in itself.
/// </summary>
public static class ZipPlanner
{
    public static ZipPlan Plan(IEnumerable<ZipSource> sources, IEnumerable<string>? excludedPaths = null, CancellationToken ct = default)
    {
        var excluded = new HashSet<string>((excludedPaths ?? []).Select(PathNames.Normalize), PathNames.Comparer);
        var used = new HashSet<string>(PathNames.Comparer);
        var entries = new List<ZipPlanEntry>();
        var missing = new List<string>();
        var renames = new List<ZipRename>();

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var path = PathNames.Normalize(source.FullPath);
            if (excluded.Contains(path)) continue;

            var isFile = source.Kind == BasketItemKind.File;
            if (isFile ? !File.Exists(path) : !Directory.Exists(path))
            {
                missing.Add(path);
                continue;
            }

            var originalName = SanitizeSegment(PathNames.GetDisplayName(path));
            var topName = MakeUnique(originalName, isFile, used);
            if (!PathNames.Comparer.Equals(topName, originalName))
                renames.Add(new ZipRename(path, originalName, topName));

            if (isFile)
            {
                var info = new FileInfo(path);
                used.Add(topName);
                entries.Add(new ZipPlanEntry(path, topName, false, SafeLength(info), SafeTime(info)));
                continue;
            }

            var dirInfo = new DirectoryInfo(path);
            used.Add(topName + "/");
            entries.Add(new ZipPlanEntry(path, topName + "/", true, 0, SafeTime(dirInfo)));

            foreach (var child in FolderScanner.Walk(path, ct))
            {
                if (excluded.Contains(child.FullPath)) continue;

                var name = topName + "/" + child.RelativePath;
                if (child.IsDirectory)
                {
                    name += "/";
                    if (!used.Add(name)) continue; // same dir twice can only differ by case: merge
                    entries.Add(new ZipPlanEntry(child.FullPath, name, true, 0, child.LastWriteTime));
                }
                else
                {
                    // Safety net for case-sensitive directories ("a.txt" and "A.txt").
                    if (used.Contains(name))
                    {
                        var dir = name[..(name.LastIndexOf('/') + 1)];
                        var leaf = name[dir.Length..];
                        var newLeaf = MakeUnique(leaf, true, used, dir);
                        renames.Add(new ZipRename(child.FullPath, name, dir + newLeaf));
                        name = dir + newLeaf;
                    }
                    used.Add(name);
                    entries.Add(new ZipPlanEntry(child.FullPath, name, false, child.Length, child.LastWriteTime));
                }
            }
        }

        return new ZipPlan { Entries = entries, MissingSources = missing, Renames = renames };
    }

    /// <summary>
    /// Returns <paramref name="name"/> or "name (n).ext" so that prefix+result is not in <paramref name="used"/>.
    /// Folders are checked with a trailing "/" and never split at the extension.
    /// </summary>
    internal static string MakeUnique(string name, bool isFile, ISet<string> used, string prefix = "")
    {
        // A file and a folder with the same name would also clash on extraction.
        bool Taken(string n) => used.Contains(prefix + n) || used.Contains(prefix + n + "/");

        if (!Taken(name)) return name;

        string stem = name, ext = "";
        if (isFile)
        {
            var dot = name.LastIndexOf('.');
            if (dot > 0) { stem = name[..dot]; ext = name[dot..]; }
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!Taken(candidate)) return candidate;
        }
    }

    /// <summary>ZIP entries use '/' as separator; a name segment must not contain one.</summary>
    private static string SanitizeSegment(string name) => name.Replace('/', '_').Replace('\\', '_');

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; } catch { return 0; }
    }

    private static DateTime SafeTime(FileSystemInfo i)
    {
        try { return i.LastWriteTime; } catch { return DateTime.Now; }
    }
}
