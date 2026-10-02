using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CXLauncher.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TargetType>))]
public enum TargetType { Executable, Shortcut }

public sealed record GameEntry
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    [JsonRequired] public TargetType TargetType { get; init; }
    public required string TargetPath { get; init; }
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string? CoverId { get; init; }
    public string Category { get; init; } = "未分类";
    public bool Favorite { get; init; }
    [JsonRequired] public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLaunchedAt { get; init; }

    public static GameEntry FromPath(string path) => new()
    {
        Name = Path.GetFileNameWithoutExtension(path),
        TargetPath = Path.GetFullPath(path),
        TargetType = Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? TargetType.Shortcut : TargetType.Executable
    };

    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 200)
            throw new InvalidDataException("游戏名称不能为空，且不能超过 200 个字符。");
        if (string.IsNullOrWhiteSpace(TargetPath) || !Path.IsPathFullyQualified(TargetPath))
            throw new InvalidDataException("游戏路径必须是完整的绝对路径。");
        var extension = Path.GetExtension(TargetPath);
        if (!Enum.IsDefined(TargetType) ||
            !extension.Equals(TargetType == TargetType.Shortcut ? ".lnk" : ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("仅支持 .exe 程序或 .lnk 快捷方式，路径与目标类型必须一致。");
        if (Arguments is null || WorkingDirectory is null || Category is null || Category.Length > 80 ||
            Arguments.Length > 32767 || TargetPath.IndexOf('\0') >= 0 || Arguments.IndexOf('\0') >= 0 || WorkingDirectory.IndexOf('\0') >= 0)
            throw new InvalidDataException("游戏字段无效或超出长度限制。");
        if (TargetType == TargetType.Shortcut && (Arguments.Length != 0 || WorkingDirectory.Length != 0))
            throw new InvalidDataException("快捷方式使用自身的参数和工作目录，请直接修改快捷方式。");
        if (WorkingDirectory.Length != 0 && !Path.IsPathFullyQualified(WorkingDirectory))
            throw new InvalidDataException("工作目录必须是完整的绝对路径。");
        if (CoverId is not null && !Guid.TryParseExact(CoverId, "N", out _))
            throw new InvalidDataException("封面标识无效。");
        if (AddedAt == default)
            throw new InvalidDataException("添加时间无效。");
    }
}

public sealed record LibraryDocument
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public List<GameEntry> Games { get; init; } = [];

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"不支持游戏库版本 {SchemaVersion}，本版支持版本 1。");
        if (Games is null || Games.Count > 10000) throw new InvalidDataException("游戏库无效或超过 10,000 个条目。");
        var ids = new HashSet<Guid>();
        foreach (var game in Games)
        {
            if (game is null) throw new InvalidDataException("游戏条目不能为空。");
            game.Validate();
            if (!ids.Add(game.Id)) throw new InvalidDataException("游戏库存在重复标识。");
        }
    }
}

public static class JsonFormat
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
}
