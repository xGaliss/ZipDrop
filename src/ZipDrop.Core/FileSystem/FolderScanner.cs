namespace ZipDrop.Core.FileSystem;

/// <summary>
/// Recursive folder walking shared by size measuring and ZIP planning.
/// Directory reparse points (symlinks/junctions) are NOT followed to avoid
/// cycles and pulling in content from outside the chosen folder.
/// </summary>
public static class FolderScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0, // include hidden/system files: the user chose this folder
        ReturnSpecialDirectories = false,
    };

    public readonly record struct Entry(string FullPath, string RelativePath, bool IsDirectory, long Length, DateTime LastWriteTime);

    /// <summary>
    /// Yields every file and every directory below <paramref name="root"/> (not the root itself),
    /// with relative paths using '/' separators. Directories are yielded before their content.
    /// </summary>
    public static IEnumerable<Entry> Walk(string root, CancellationToken ct = default)
    {
        var stack = new Stack<(string Dir, string Rel)>();
        stack.Push((root, ""));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, rel) = stack.Pop();

            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Options).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var child in children)
            {
                var childRel = rel.Length == 0 ? child.Name : rel + "/" + child.Name;
                if (child is DirectoryInfo d)
                {
                    yield return new Entry(d.FullName, childRel, true, 0, SafeTime(d));
                    if ((d.Attributes & FileAttributes.ReparsePoint) == 0)
                        stack.Push((d.FullName, childRel));
                }
                else if (child is FileInfo f)
                {
                    yield return new Entry(f.FullName, childRel, false, SafeLength(f), SafeTime(f));
                }
            }
        }
    }

    public static long MeasureBytes(string root, CancellationToken ct = default)
    {
        long total = 0;
        foreach (var e in Walk(root, ct))
            if (!e.IsDirectory) total += e.Length;
        return total;
    }

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; } catch { return 0; }
    }

    private static DateTime SafeTime(FileSystemInfo i)
    {
        try { return i.LastWriteTime; } catch { return DateTime.Now; }
    }
}
