using System.IO;
using System.Text.Json;
using CXLauncher.Models;

namespace CXLauncher.Services;

public sealed class DataFileException(string path, string message, Exception cause)
    : IOException(message, cause)
{
    public string FilePath { get; } = path;
}

// Each file has one store for the lifetime of the application. Every write and recovery is serialized.
public sealed class AtomicJsonStore<T>(string path, string backupDirectory, Func<T> createDefault, Action<T> validate)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string FilePath => path;

    public async Task<T> LoadAsync()
    {
        await gate.WaitAsync();
        try { return File.Exists(path) ? await ReadAsync(path) : createDefault(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        { throw new DataFileException(path, $"{Path.GetFileName(path)} 无法读取：{ex.Message} 原文件已保留。", ex); }
        finally { gate.Release(); }
    }

    public async Task<T> ReadAsync(string source)
    {
        var info = new FileInfo(source);
        if (info.Length > 32 * 1024 * 1024) throw new InvalidDataException("配置文件超过 32 MB。");
        await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonFormat.Options)
            ?? throw new InvalidDataException("文件内容不能为空。");
        validate(value);
        return value;
    }

    public async Task SaveAsync(T value)
    {
        validate(value);
        await gate.WaitAsync();
        try { await WriteAsync(value); }
        finally { gate.Release(); }
    }

    private async Task WriteAsync(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(backupDirectory);
        // Do not replace a corrupt or newer-schema file with an empty/default document.
        if (File.Exists(path)) _ = await ReadAsync(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonFormat.Options);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                var backup = Path.Combine(backupDirectory, Path.GetFileName(path) + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + ".bak");
                File.Replace(temporary, path, backup);
            }
            else File.Move(temporary, path);
            foreach (var obsolete in Directory.GetFiles(backupDirectory, Path.GetFileName(path) + ".*.bak")
                .OrderByDescending(File.GetLastWriteTimeUtc).Skip(5)) File.Delete(obsolete);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<T?> FindLatestBackupAsync()
    {
        if (!Directory.Exists(backupDirectory)) return default;
        foreach (var candidate in Directory.GetFiles(backupDirectory, Path.GetFileName(path) + ".*.bak").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try { return await ReadAsync(candidate); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        }
        return default;
    }

    public async Task RecoverAsync(T value)
    {
        validate(value);
        await gate.WaitAsync();
        string? preserved = null;
        try
        {
            if (File.Exists(path))
            {
                preserved = path + ".preserved-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff");
                File.Move(path, preserved);
            }
            try { await WriteAsync(value); }
            catch { if (preserved is not null && !File.Exists(path)) File.Move(preserved, path); throw; }
        }
        finally { gate.Release(); }
    }
}
