using System.IO;

namespace CXLauncher.Services;

public sealed class DiagnosticLog(AppPaths paths)
{
    private readonly object gate = new();
    public void Write(string operation, Exception error) => WriteLine($"{operation}: {error.GetType().Name} 0x{error.HResult:X8}");
    public void Ready(int games) => WriteLine($"Ready: {games} games");
    private void WriteLine(string message)
    {
        // Keep arguments and user paths out of the default diagnostic log.
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(paths.Logs);
                var file = Path.Combine(paths.Logs, "launcher.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024)
                {
                    for (var i = 2; i >= 1; i--)
                    {
                        var source = Path.Combine(paths.Logs, $"launcher.{i}.log");
                        if (File.Exists(source)) File.Move(source, Path.Combine(paths.Logs, $"launcher.{i + 1}.log"), true);
                    }
                    File.Move(file, Path.Combine(paths.Logs, "launcher.1.log"), true);
                }
                File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
