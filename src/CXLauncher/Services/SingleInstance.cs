using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace CXLauncher.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    public bool IsPrimary { get; }

    public SingleInstance(string dataRoot)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + Path.GetFullPath(dataRoot).ToUpperInvariant())))[..24];
        pipeName = "CXLauncher-" + key;
        mutex = new Mutex(true, "Local\\" + pipeName, out var created);
        IsPrimary = created;
    }

    public void Listen(Action activate) => _ = Task.Run(async () =>
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(shutdown.Token);
                var buffer = new byte[8];
                var read = await pipe.ReadAsync(buffer, shutdown.Token);
                if (read != 0) activate();
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(100, shutdown.Token).ConfigureAwait(false); }
        }
    });

    public async Task ActivatePrimaryAsync()
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2500);
            await pipe.WriteAsync(new byte[] { 1 });
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose(); shutdown.Dispose();
    }
}
