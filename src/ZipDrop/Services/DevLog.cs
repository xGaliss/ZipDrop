using System.IO;

namespace ZipDrop.Services;

/// <summary>
/// Opt-in development trace: set the environment variable ZIPDROP_TRACE=1 and events
/// (drag/drop, shake, hotkey) are appended to %LOCALAPPDATA%\ZipDrop\trace.log.
/// Off by default; never leaves the machine.
/// </summary>
internal static class DevLog
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("ZIPDROP_TRACE") == "1";
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipDrop", "trace.log");

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}\n");
            }
        }
        catch { /* tracing must never throw */ }
    }
}
