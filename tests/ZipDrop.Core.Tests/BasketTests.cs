using ZipDrop.Core.Baskets;

namespace ZipDrop.Core.Tests;

public class BasketTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Add_files_and_folders_counts_items()
    {
        var basket = new Basket();
        var foto = _tmp.File("Desktop/foto.jpg", "12345");
        var pdf = _tmp.File("Documents/contrato.pdf", "abc");
        var proj = _tmp.Dir("Work/proyecto");

        var result = basket.Add([foto, pdf, proj]);

        Assert.Equal(3, result.Added.Count);
        Assert.Equal(3, basket.Count);
        Assert.Equal(BasketItemKind.Folder, basket.Items[2].Kind);
        Assert.Equal(8, basket.TotalBytes);
    }

    [Fact]
    public void Same_path_is_never_added_twice_even_with_different_case_or_trailing_slash()
    {
        var basket = new Basket();
        var file = _tmp.File("a/File.txt");
        var dir = _tmp.Dir("Folder");

        basket.Add([file, dir]);
        var second = basket.Add([file.ToUpperInvariant(), dir + "\\", dir.ToLowerInvariant()]);

        Assert.Empty(second.Added);
        Assert.Equal(3, second.Duplicates.Count);
        Assert.Equal(2, basket.Count);
    }

    [Fact]
    public void Duplicates_inside_one_drop_are_collapsed()
    {
        var basket = new Basket();
        var file = _tmp.File("x.txt");
        var result = basket.Add([file, file]);
        Assert.Single(result.Added);
        Assert.Single(result.Duplicates);
    }

    [Fact]
    public void Nonexistent_paths_are_reported_not_added()
    {
        var basket = new Basket();
        var result = basket.Add([_tmp.PathOf("nope.txt"), "", "   "]);
        Assert.Empty(result.Added);
        Assert.Single(result.NotFound);
        Assert.True(basket.IsEmpty);
    }

    [Fact]
    public void Missing_files_are_detected_and_can_be_recovered()
    {
        var basket = new Basket();
        var a = _tmp.File("a.txt");
        var b = _tmp.File("b.txt");
        basket.Add([a, b]);

        File.Delete(a);
        Assert.True(basket.RefreshExistence());
        Assert.Equal(1, basket.MissingCount);
        Assert.True(basket.Items.Single(i => i.FullPath == a).IsMissing);
        Assert.Equal(2, basket.Count); // still listed so the user can see which one

        File.WriteAllText(a, "back");
        Assert.True(basket.RefreshExistence());
        Assert.Equal(0, basket.MissingCount);
        Assert.False(basket.RefreshExistence());
    }

    [Fact]
    public void Moved_folder_is_missing()
    {
        var basket = new Basket();
        var dir = _tmp.Dir("proj");
        basket.Add([dir]);
        Directory.Move(dir, _tmp.PathOf("proj2"));
        basket.RefreshExistence();
        Assert.Equal(1, basket.MissingCount);
    }

    [Fact]
    public void RemoveMissing_and_Clear()
    {
        var basket = new Basket();
        var a = _tmp.File("a.txt");
        var b = _tmp.File("b.txt");
        basket.Add([a, b]);
        File.Delete(a);
        basket.RefreshExistence();

        Assert.Equal(1, basket.RemoveMissing());
        Assert.Equal(1, basket.Count);

        // Removed path can be added again later.
        File.WriteAllText(a, "x");
        Assert.Single(basket.Add([a]).Added);

        basket.Clear();
        Assert.True(basket.IsEmpty);
        Assert.Single(basket.Add([a]).Added);
    }

    [Fact]
    public async Task Folder_size_is_measured_in_background()
    {
        var basket = new Basket();
        _tmp.File("p/one.bin", new string('a', 100));
        _tmp.File("p/sub/two.bin", new string('b', 50));
        basket.Add([_tmp.PathOf("p")]);

        Assert.True(basket.IsMeasuring);
        await basket.MeasurePendingAsync();
        Assert.False(basket.IsMeasuring);
        Assert.Equal(150, basket.TotalBytes);
    }

    [Fact]
    public void Aggregate_change_notifications_are_raised()
    {
        var basket = new Basket();
        var changed = new List<string?>();
        basket.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        basket.Add([_tmp.File("a.txt")]);
        Assert.Contains(nameof(Basket.Count), changed);
        Assert.Contains(nameof(Basket.TotalBytes), changed);
    }
}
