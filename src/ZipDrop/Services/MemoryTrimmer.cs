using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ZipDrop.Services;

/// <summary>
/// ZipDrop spends almost all its life idle in the tray. After startup and whenever the
/// overlay hides, compact the GC heap and hand unused pages back to Windows so the
/// resident footprint stays small. Pages are faulted back in transparently when needed.
/// </summary>
internal sealed class MemoryTrimmer
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(4);
    private readonly DispatcherTimer _timer;

    public MemoryTrimmer()
    {
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = IdleDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Trim();
        };
    }

    /// <summary>(Re)starts the idle countdown.</summary>
    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel() => _timer.Stop();

    private static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        try { EmptyWorkingSet(Process.GetCurrentProcess().Handle); }
        catch (EntryPointNotFoundException) { }
        DevLog.Write("Memory trimmed");
    }

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}
