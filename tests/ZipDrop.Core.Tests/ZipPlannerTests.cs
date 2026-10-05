using ZipDrop.Core.Archiving;
using ZipDrop.Core.Baskets;

namespace ZipDrop.Core.Tests;

public class ZipPlannerTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private static ZipSource F(string p) => new(p, BasketItemKind.File);
    private static ZipSource D(string p) => new(p, BasketItemKind.Folder);

    private static List<string> Names(ZipPlan plan) => plan.Entries.Select(e => e.EntryName).ToList();

    [Fact]
    public void Files_and_folders_become_top_level_entries_with_structure()
    {
        var foto = _tmp.File("Desktop/foto.jpg");
        var pdf = _tmp.File("Documents/contrato.pdf");
        _tmp.File("Other/proyecto/src/main.cs");
        _tmp.Dir("Other/proyecto/empty");

        var plan = ZipPlanner.Plan([F(foto), F(pdf), D(_tmp.PathOf("Other/proyecto"))]);

        var names = Names(plan);
        Assert.Contains("foto.jpg", names);
        Assert.Contains("contrato.pdf", names);
        Assert.Contains("proyecto/", names);
        Assert.Contains("proyecto/src/", names);
        Assert.Contains("proyecto/src/main.cs", names);
        Assert.Contains("proyecto/empty/", names); // empty folders are preserved
        Assert.All(names, n => Assert.DoesNotContain('\\', n));
        Assert.Empty(plan.Renames);
    }

    [Fact]
    public void Same_file_name_from_different_folders_is_renamed_never_overwritten()
    {
        var a = _tmp.File("A/report.pdf");
        var b = _tmp.File("B/report.pdf");
        var c = _tmp.File("C/REPORT.pdf");

        var plan = ZipPlanner.Plan([F(a), F(b), F(c)]);

        Assert.Equal(["report.pdf", "report (2).pdf", "REPORT (3).pdf"], Names(plan));
        Assert.Equal(2, plan.Renames.Count);
        Assert.Equal(Path.GetFullPath(b), plan.Renames[0].SourcePath);
    }

    [Fact]
    public void Folder_name_conflicts_rename_the_whole_folder()
    {
        _tmp.File("A/proj/x.txt");
        _tmp.File("B/proj/x.txt");

        var plan = ZipPlanner.Plan([D(_tmp.PathOf("A/proj")), D(_tmp.PathOf("B/proj"))]);

        var names = Names(plan);
        Assert.Contains("proj/x.txt", names);
        Assert.Contains("proj (2)/x.txt", names);
    }

    [Fact]
    public void File_and_folder_with_same_name_do_not_clash()
    {
        var file = _tmp.File("A/data");
        _tmp.File("B/data/inner.txt");

        var plan = ZipPlanner.Plan([F(file), D(_tmp.PathOf("B/data"))]);

        var names = Names(plan);
        Assert.Contains("data", names);
        Assert.Contains("data (2)/inner.txt", names);
    }

    [Theory]
    [InlineData("archive.tar.gz", "archive.tar (2).gz")]
    [InlineData("README", "README (2)")]
    [InlineData(".gitignore", ".gitignore (2)")]
    public void MakeUnique_keeps_extension(string name, string expected)
    {
        var used = new HashSet<string>(PathNames.Comparer) { name };
        Assert.Equal(expected, ZipPlanner.MakeUnique(name, true, used));
    }

    [Fact]
    public void MakeUnique_skips_taken_numbers()
    {
        var used = new HashSet<string>(PathNames.Comparer) { "a.txt", "a (2).txt", "a (3).txt" };
        Assert.Equal("a (4).txt", ZipPlanner.MakeUnique("a.txt", true, used));
    }

    [Fact]
    public void Missing_sources_are_reported_not_thrown()
    {
        var ok = _tmp.File("ok.txt");
        var plan = ZipPlanner.Plan([F(ok), F(_tmp.PathOf("gone.txt")), D(_tmp.PathOf("gone-dir"))]);
        Assert.Equal(["ok.txt"], Names(plan));
        Assert.Equal(2, plan.MissingSources.Count);
    }

    [Fact]
    public void Destination_zip_is_excluded_when_saved_inside_an_added_folder()
    {
        _tmp.File("Docs/a.txt");
        var dest = _tmp.File("Docs/Archive.zip", "old zip");

        var plan = ZipPlanner.Plan([D(_tmp.PathOf("Docs"))], [dest]);

        Assert.DoesNotContain("Docs/Archive.zip", Names(plan));
        Assert.Contains("Docs/a.txt", Names(plan));
    }

    [Fact]
    public void Unicode_names_are_preserved()
    {
        var f = _tmp.File("ñandú/日本語 ファイル 📦.txt");
        var plan = ZipPlanner.Plan([D(_tmp.PathOf("ñandú"))]);
        Assert.Contains("ñandú/日本語 ファイル 📦.txt", Names(plan));
        Assert.True(File.Exists(f));
    }

    [Fact]
    public void File_contained_in_an_added_folder_is_also_added_at_top_level()
    {
        // Documented behaviour (D-006): the user explicitly added both, so both are kept.
        var inner = _tmp.File("proj/readme.md");
        var plan = ZipPlanner.Plan([D(_tmp.PathOf("proj")), F(inner)]);
        Assert.Contains("proj/readme.md", Names(plan));
        Assert.Contains("readme.md", Names(plan));
    }

    [Fact]
    public void Drive_root_gets_a_usable_name()
    {
        Assert.Equal("C", PathNames.GetDisplayName(@"C:\"));
    }
}
