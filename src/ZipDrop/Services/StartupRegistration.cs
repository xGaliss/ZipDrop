using System.IO;
using Microsoft.Win32;

namespace ZipDrop.Services;

/// <summary>"Launch at startup" via HKCU\...\Run (per user, no admin rights needed).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZipDrop";
    public const string BackgroundArg = "--background";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\" {BackgroundArg}"); // also refreshes the path if the exe moved
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Policy-locked registry: setting simply has no effect. Surfaced in KNOWN_ISSUES.
        }
    }
}
