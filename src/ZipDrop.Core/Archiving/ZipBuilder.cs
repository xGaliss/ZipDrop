using System.Diagnostics;
using System.IO.Compression;

namespace ZipDrop.Core.Archiving;

public readonly record struct ZipProgress(long BytesDone, long BytesTotal, int FilesDone, int FilesTotal, string? CurrentEntry)
{
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
        : FilesTotal > 0 ? (double)FilesDone / FilesTotal : 0;
}

public sealed record ZipSkippedFile(string SourcePath, string Reason);

public sealed record ZipResult(
    string DestinationPath,
    int FilesWritten,
    long BytesWritten,
    IReadOnlyList<ZipSkippedFile> Skipped,
    IReadOnlyList<string> MissingSources,
    IReadOnlyList<ZipRename> Renames);

/// <summary>
/// Writes a <see cref="ZipPlan"/> to disk.
/// The archive is written to a temporary file next to the destination and moved
/// into place only on success, so a cancelled or failed run never leaves a broken ZIP
/// (and never destroys an existing file the user chose to overwrite).
/// </summary>
public static class ZipBuilder
{
    private const int BufferSize = 1024 * 1024;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(50);
    private static readonly DateTime ZipMinDate = new(1980, 1, 1, 0, 0, 0);
    private static readonly DateTime ZipMaxDate = new(2107, 12, 31, 23, 59, 58);

    /// <summary>Plans and builds on a background thread. Safe to call from the UI thread.</summary>
    public static Task<ZipResult> CreateAsync(
        IEnumerable<ZipSource> sources,
        string destinationPath,
        IProgress<ZipProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sourceList = sources.ToList();
        return Task.Run(() =>
        {
            var plan = ZipPlanner.Plan(sourceList, [destinationPath], ct);
            return Build(plan, destinationPath, progress, ct);
        }, ct);
    }

    public static ZipResult Build(ZipPlan plan, string destinationPath, IProgress<ZipProgress>? progress, CancellationToken ct)
    {
        if (plan.Entries.Count == 0)
            throw new InvalidOperationException("There is nothing to zip: every item in the basket is missing.");

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Invalid destination", nameof(destinationPath));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.zipdrop-tmp");
        var skipped = new List<ZipSkippedFile>();
        long bytesDone = 0;
        int filesDone = 0;
        var filesTotal = plan.FileCount;
        var bytesTotal = plan.TotalBytes;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.MinValue;

        void Report(string? current, bool force = false)
        {
            if (progress is null) return;
            if (!force && clock.Elapsed - lastReport < ReportInterval) return;
            lastReport = clock.Elapsed;
            progress.Report(new ZipProgress(bytesDone, bytesTotal, filesDone, filesTotal, current));
        }

        try
        {
            using (var zipStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.SequentialScan))
            {
                File.SetAttributes(tempPath, FileAttributes.Hidden);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: null);
                var buffer = new byte[BufferSize];
                Report(null, force: true);

                foreach (var entry in plan.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        var dirEntry = archive.CreateEntry(entry.EntryName, CompressionLevel.NoCompression);
                        dirEntry.LastWriteTime = ClampTime(entry.LastWriteTime);
                        continue;
                    }

                    // Open the source BEFORE creating the entry: a locked/vanished file is skipped
                    // cleanly instead of leaving a half-written entry inside the archive.
                    FileStream source;
                    try
                    {
                        source = new FileStream(entry.SourcePath!, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add(new ZipSkippedFile(entry.SourcePath!, ex.Message));
                        bytesDone += entry.Length;
                        Report(entry.EntryName);
                        continue;
                    }

                    using (source)
                    {
                        var zipEntry = archive.CreateEntry(entry.EntryName, CompressionPolicy.ForFile(entry.EntryName));
                        zipEntry.LastWriteTime = ClampTime(entry.LastWriteTime);
                        using var target = zipEntry.Open();
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            target.Write(buffer, 0, read);
                            bytesDone += read;
                            Report(entry.EntryName);
                        }
                    }
                    filesDone++;
                    Report(entry.EntryName);
                }
            }

            ct.ThrowIfCancellationRequested();
            File.SetAttributes(tempPath, FileAttributes.Normal);
            File.Move(tempPath, destination, overwrite: true);
            Report(null, force: true);

            return new ZipResult(destination, filesDone, bytesDone, skipped, plan.MissingSources, plan.Renames);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static DateTimeOffset ClampTime(DateTime t) =>
        t < ZipMinDate ? ZipMinDate : t > ZipMaxDate ? ZipMaxDate : t;

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
