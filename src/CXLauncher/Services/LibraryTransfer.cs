using System.IO;
using System.Text.Json;
using CXLauncher.Models;

namespace CXLauncher.Services;

public sealed record ImportPreview(LibraryDocument Merged, int Added, int Skipped, int MissingCovers);

public static class LibraryTransfer
{
    public static ImportPreview Preview(LibraryDocument current, LibraryDocument incoming)
    {
        current.Validate(); incoming.Validate();
        var paths = new HashSet<string>(current.Games.Select(g => Path.GetFullPath(g.TargetPath)), StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>(current.Games.Select(g => g.Id));
        var merged = current.Games.ToList();
        int skipped = 0, covers = 0;
        foreach (var game in incoming.Games)
        {
            if (!paths.Add(Path.GetFullPath(game.TargetPath))) { skipped++; continue; }
            var item = ids.Add(game.Id) ? game : game with { Id = Guid.NewGuid() };
            // JSON is intentionally metadata-only. Never trust an imported cover identifier to point at local files.
            if (item.CoverId is not null) covers++;
            merged.Add(item with { CoverId = null });
        }
        var document = new LibraryDocument { Games = merged };
        document.Validate();
        return new ImportPreview(document, merged.Count - current.Games.Count, skipped, covers);
    }

    public static async Task<LibraryDocument> ReadAsync(string path)
    {
        var store = new AtomicJsonStore<LibraryDocument>(path, "", () => new(), d => d.Validate());
        return await store.ReadAsync(path);
    }

    public static async Task ExportAsync(LibraryDocument document, string path)
    {
        document.Validate();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            { await JsonSerializer.SerializeAsync(stream, document, JsonFormat.Options); await stream.FlushAsync(); stream.Flush(true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
