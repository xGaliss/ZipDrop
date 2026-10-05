using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ZipDrop.Services;

/// <summary>
/// One ZipDrop per user session. A second launch forwards its request to the first
/// instance over a per-user named pipe and exits:
///   ZipDrop.exe                 -> show the basket
///   ZipDrop.exe a.txt "C:\dir"  -> add those paths to the basket (Send To, scripts)
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Id = $"ZipDrop.{Environment.UserName}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\" + Id, out var createdNew);
        IsFirst = createdNew;
    }

    public bool IsFirst { get; }

    /// <summary>First instance: receive requests from later launches. Callback runs on a pool thread.</summary>
    public void Listen(Action<IReadOnlyList<string>> onRequest)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(_cts.Token);
                    var paths = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    onRequest(paths);
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { await Task.Delay(200); }
            }
        });
    }

    /// <summary>Later instance: send args (possibly empty = "show") to the first one.</summary>
    public bool Forward(IEnumerable<string> paths)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var p in paths) writer.Write(Path.GetFullPath(p) + "\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
