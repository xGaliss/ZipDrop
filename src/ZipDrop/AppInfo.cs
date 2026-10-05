using System.Reflection;

namespace ZipDrop;

internal static class AppInfo
{
    /// <summary>"0.1.1" (or "0.2.0-rc.1"), without the "+commit" build metadata .NET appends.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "?";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
