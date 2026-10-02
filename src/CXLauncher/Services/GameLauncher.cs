using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CXLauncher.Models;

namespace CXLauncher.Services;

public sealed record LaunchResult(bool Success, string Message, DateTimeOffset? SubmittedAt = null);

public sealed class GameLauncher
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> lastRequest = new();

    public static ProcessStartInfo CreateStartInfo(GameEntry game)
    {
        game.Validate();
        return game.TargetType == TargetType.Shortcut
            ? new ProcessStartInfo(game.TargetPath) { UseShellExecute = true }
            : new ProcessStartInfo(game.TargetPath)
            {
                UseShellExecute = false,
                Arguments = game.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory)
                    ? Path.GetDirectoryName(game.TargetPath)! : game.WorkingDirectory
            };
    }

    public Task<LaunchResult> LaunchAsync(GameEntry game)
    {
        var now = DateTimeOffset.UtcNow;
        while (true)
        {
            if (lastRequest.TryGetValue(game.Id, out var previous))
            {
                if (now - previous < TimeSpan.FromSeconds(2)) return Task.FromResult(new LaunchResult(false, "启动请求已提交，请稍候再试。"));
                if (lastRequest.TryUpdate(game.Id, now, previous)) break;
            }
            else if (lastRequest.TryAdd(game.Id, now)) break;
        }
        return Task.Run(() =>
        {
            try
            {
                var info = CreateStartInfo(game);
                if (!File.Exists(game.TargetPath)) return new LaunchResult(false, "找不到游戏文件。请在编辑中重新选择路径。");
                if (game.TargetType == TargetType.Executable && !Directory.Exists(info.WorkingDirectory))
                    return new LaunchResult(false, "工作目录不存在。请在编辑的高级设置中重新选择。");
                using var process = Process.Start(info);
                return new LaunchResult(true, $"已向系统提交「{game.Name}」的启动请求。", DateTimeOffset.UtcNow);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
            { return new LaunchResult(false, "此程序需要管理员权限。请检查程序的权限要求或使用其快捷方式。"); }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { return new LaunchResult(false, $"启动失败：{ex.Message}"); }
        });
    }
}
