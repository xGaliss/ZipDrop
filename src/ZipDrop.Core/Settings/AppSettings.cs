using System.Text.Json;
using System.Text.Json.Serialization;
using ZipDrop.Core.Gestures;

namespace ZipDrop.Core.Settings;

/// <summary>The complete MVP settings surface. Keep it small on purpose.</summary>
public sealed record AppSettings
{
    public bool LaunchAtStartup { get; init; }
    public bool ShakeEnabled { get; init; } = true;
    public ShakeSensitivity ShakeSensitivity { get; init; } = ShakeSensitivity.Medium;

    /// <summary>Human-readable, e.g. "Ctrl+Shift+Z". Parsed by the app layer.</summary>
    public string GlobalShortcut { get; init; } = "Ctrl+Shift+Z";

    public bool ClearBasketAfterZip { get; init; } = true;
}

/// <summary>JSON persistence in %APPDATA%\ZipDrop\settings.json. Never throws on load.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZipDrop", "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
