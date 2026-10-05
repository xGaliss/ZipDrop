using System.IO.Compression;
using ZipDrop.Core.Archiving;
using ZipDrop.Core.Baskets;

namespace ZipDrop.Core.Tests;

public class ZipBuilderTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private static Dictionary<string, string> ReadZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e => e.FullName.EndsWith('/') ? "" : new StreamReader(e.Open()).ReadToEnd());
    }

    [Fact]
    public async Task Success_criterion_three_items_from_three_places()
    {
        var foto = _tmp.File("Desktop/foto.jpg", "JPEG");
        var pdf = _tmp.File("Documents/contrato.pdf", "PDF");
        _tmp.File("Elsewhere/proyecto/src/app.cs", "code");
        _tmp.File("Elsewhere/proyecto/README.md", "readme");
        var dest = _tmp.PathOf("Out/Archive.zip");

        var basket = new Basket();
        basket.Add([foto, pdf, _tmp.PathOf("Elsewhere/proyecto")]);

        var result = await ZipBuilder.CreateAsync(
            basket.Items.Select(i => new ZipSource(i.FullPath, i.Kind)), dest);

        var entries = ReadZip(dest);
        Assert.Equal("JPEG", entries["foto.jpg"]);
        Assert.Equal("PDF", entries["contrato.pdf"]);
        Assert.Equal("code", entries["proyecto/src/app.cs"]);
        Assert.Equal("readme", entries["proyecto/README.md"]);
        Assert.Equal(4, result.FilesWritten);
        Assert.Empty(result.Skipped);
        Assert.Empty(Directory.GetFiles(_tmp.PathOf("Out"), "*.zipdrop-tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Conflicting_names_keep_both_contents()
    {
        var a = _tmp.File("A/same.txt", "first");
        var b = _tmp.File("B/same.txt", "second");
        var dest = _tmp.PathOf("out.zip");

        var result = await ZipBuilder.CreateAsync([new(a, BasketItemKind.File), new(b, BasketItemKind.File)], dest);

        var entries = ReadZip(dest);
        Assert.Equal("first", entries["same.txt"]);
        Assert.Equal("second", entries["same (2).txt"]);
        Assert.Single(result.Renames);
    }

    [Fact]
    public async Task Unicode_entry_names_round_trip()
    {
        var f = _tmp.File("src/Café ñ 日本.txt", "hola");
        var dest = _tmp.PathOf("u.zip");
        await ZipBuilder.CreateAsync([new(f, BasketItemKind.File)], dest);
        Assert.Equal("hola", ReadZip(dest)["Café ñ 日本.txt"]);
    }

    [Fact]
    public async Task Missing_item_is_skipped_and_reported()
    {
        var ok = _tmp.File("ok.txt", "ok");
        var gone = _tmp.PathOf("gone.txt");
        var dest = _tmp.PathOf("m.zip");

        var result = await ZipBuilder.CreateAsync([new(ok, BasketItemKind.File), new(gone, BasketItemKind.File)], dest);

        Assert.Single(ReadZip(dest));
        Assert.Equal([gone], result.MissingSources);
    }

    [Fact]
    public async Task All_items_missing_fails_without_creating_file()
    {
        var dest = _tmp.PathOf("none.zip");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ZipBuilder.CreateAsync([new(_tmp.PathOf("x"), BasketItemKind.File)], dest));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task Cancellation_leaves_no_partial_zip_and_keeps_existing_file()
    {
        var big = _tmp.PathOf("big.bin");
        await using (var fs = File.Create(big)) fs.SetLength(64L * 1024 * 1024);
        var dest = _tmp.File("existing.zip", "keep me");

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.BytesDone > 0) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ZipBuilder.CreateAsync([new(big, BasketItemKind.File)], dest, progress, cts.Token));

        Assert.Equal("keep me", File.ReadAllText(dest));
        Assert.Empty(Directory.GetFiles(_tmp.Root, "*.zipdrop-tmp"));
    }

    [Fact]
    public async Task Progress_reaches_total()
    {
        var a = _tmp.File("a.txt", new string('a', 5000));
        var b = _tmp.File("b.txt", new string('b', 3000));
        ZipProgress last = default;
        var progress = new SyncProgress(p => last = p);

        await ZipBuilder.CreateAsync([new(a, BasketItemKind.File), new(b, BasketItemKind.File)], _tmp.PathOf("p.zip"), progress);

        Assert.Equal(8000, last.BytesTotal);
        Assert.Equal(8000, last.BytesDone);
        Assert.Equal(1.0, last.Fraction);
    }

    [Fact]
    public async Task Zip_saved_inside_added_folder_does_not_include_itself()
    {
        _tmp.File("Docs/a.txt", "a");
        var dest = _tmp.PathOf("Docs/Archive.zip");

        await ZipBuilder.CreateAsync([new(_tmp.PathOf("Docs"), BasketItemKind.Folder)], dest);

        var names = ReadZip(dest).Keys;
        Assert.Contains("Docs/a.txt", names);
        Assert.DoesNotContain(names, n => n.Contains("Archive.zip") || n.Contains("zipdrop-tmp"));
    }

    [Fact]
    public async Task Long_paths_are_supported()
    {
        var segments = Enumerable.Range(0, 12).Select(i => $"very-long-folder-name-number-{i:00}");
        var relative = Path.Combine([.. segments, "deep file.txt"]);
        var file = _tmp.File(relative, "deep");
        Assert.True(file.Length > 260);

        var dest = _tmp.PathOf("long.zip");
        await ZipBuilder.CreateAsync([new(_tmp.PathOf("very-long-folder-name-number-00"), BasketItemKind.Folder)], dest);

        Assert.Contains(ReadZip(dest), kv => kv.Key.EndsWith("deep file.txt") && kv.Value == "deep");
    }

    [Fact]
    public async Task Already_compressed_files_are_stored()
    {
        var jpg = _tmp.File("p.jpg", new string('a', 10_000));
        var txt = _tmp.File("t.txt", new string('a', 10_000));
        var dest = _tmp.PathOf("c.zip");
        await ZipBuilder.CreateAsync([new(jpg, BasketItemKind.File), new(txt, BasketItemKind.File)], dest);

        using var zip = ZipFile.OpenRead(dest);
        Assert.Equal(10_000, zip.GetEntry("p.jpg")!.CompressedLength);
        Assert.True(zip.GetEntry("t.txt")!.CompressedLength < 1_000);
    }

    /// <summary>Progress&lt;T&gt; posts asynchronously; tests need synchronous callbacks.</summary>
    private sealed class SyncProgress(Action<ZipProgress> action) : IProgress<ZipProgress>
    {
        public void Report(ZipProgress value) => action(value);
    }
}
