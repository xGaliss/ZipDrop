namespace ZipDrop.Core.Baskets;

/// <summary>Path normalization helpers shared by the basket and the ZIP planner.</summary>
public static class PathNames
{
    /// <summary>
    /// Windows paths are case-insensitive, and so is extraction with Explorer,
    /// so every comparison in ZipDrop is OrdinalIgnoreCase.
    /// </summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Full path without trailing separators (except for drive roots like "C:\").</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (full.Length > (root?.Length ?? 0))
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>Name shown in the UI and used as top-level ZIP entry name.</summary>
    public static string GetDisplayName(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        if (!string.IsNullOrEmpty(name)) return name;

        // Drive root ("D:\") or UNC share root.
        var root = Path.GetPathRoot(fullPath) ?? fullPath;
        var cleaned = root.Trim('\\', '/', ':');
        if (cleaned.Length == 0) return "root";
        // "\\server\share" -> "server_share"; "D" -> "D"
        return cleaned.Replace('\\', '_').Replace('/', '_').Replace(":", "");
    }

    /// <summary>True if <paramref name="path"/> equals or is inside <paramref name="folder"/>.</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        if (Comparer.Equals(path, folder)) return true;
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
