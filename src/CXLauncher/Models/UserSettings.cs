using System.IO;
using System.Text.Json.Serialization;

namespace CXLauncher.Models;

public sealed record UserSettings
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    public double Width { get; init; } = 1180;
    public double Height { get; init; } = 760;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public bool Maximized { get; init; }
    public bool ListView { get; init; }
    public int SortMode { get; init; }

    public void Validate()
    {
        if (SchemaVersion != 1 || !double.IsFinite(Width) || !double.IsFinite(Height) ||
            Width < 860 || Width > 16000 || Height < 560 || Height > 16000 || SortMode is < 0 or > 2 ||
            (Left is { } left && !double.IsFinite(left)) || (Top is { } top && !double.IsFinite(top)))
            throw new InvalidDataException("窗口设置格式或版本无效。");
    }
}
