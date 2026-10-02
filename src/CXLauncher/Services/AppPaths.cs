using System.IO;

namespace CXLauncher.Services;

public sealed class AppPaths
{
    public string Root { get; }
    public string Covers => Path.Combine(Root, "covers");
    public string Thumbnails => Path.Combine(Root, "cache", "thumbnails");
    public string Backups => Path.Combine(Root, "backups");
    public string Logs => Path.Combine(Root, "logs");

    public AppPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Environment.GetEnvironmentVariable("CXLAUNCHER_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CXLauncher"));
    }

    public void EnsureDirectories()
    {
        foreach (var directory in new[] { Root, Covers, Thumbnails, Backups, Logs }) Directory.CreateDirectory(directory);
    }

    public string? CoverPath(string? id) => id is not null && Guid.TryParseExact(id, "N", out _)
        ? Path.Combine(Covers, id + ".png") : null;
}
