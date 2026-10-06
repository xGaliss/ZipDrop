using ZipDrop.Core.Formatting;
using ZipDrop.Core.Gestures;
using ZipDrop.Core.Settings;

namespace ZipDrop.Core.Tests;

public class MiscTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(143L * 1024 * 1024, "143 MB")]
    [InlineData(1503238553L, "1.4 GB")]
    public void Size_formatting(long bytes, string expected) => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Fact]
    public void Size_formatting_uses_the_given_culture()
    {
        var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        Assert.Equal("1,4 GB", SizeFormatter.Format(1503238553L, es));
        Assert.Equal("143 MB", SizeFormatter.Format(143L * 1024 * 1024, es));
    }

    [Fact]
    public void Item_pluralization()
    {
        Assert.Equal("1 item", SizeFormatter.Items(1));
        Assert.Equal("8 items", SizeFormatter.Items(8));
    }

    [Fact]
    public void Settings_round_trip_and_defaults()
    {
        var store = new SettingsStore(Path.Combine(_tmp.Root, "s", "settings.json"));
        Assert.Equal("Ctrl+Alt+Z", store.Load().GlobalShortcut);

        store.Save(new AppSettings { ShakeSensitivity = ShakeSensitivity.High, GlobalShortcut = "Ctrl+Alt+D" });
        var loaded = store.Load();
        Assert.Equal(ShakeSensitivity.High, loaded.ShakeSensitivity);
        Assert.Equal("Ctrl+Alt+D", loaded.GlobalShortcut);
        Assert.True(loaded.ClearBasketAfterZip);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        var path = _tmp.File("settings.json", "{ not json");
        Assert.True(new SettingsStore(path).Load().ShakeEnabled);
    }
}
