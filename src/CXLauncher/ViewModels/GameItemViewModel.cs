using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CXLauncher.Models;
using CXLauncher.Services;

namespace CXLauncher.ViewModels;

public partial class GameItemViewModel(GameEntry entry, CoverService covers) : ObservableObject
{
    private int visualUsers;
    public event Action? VisualChanged;
    public GameEntry Entry { get; private set; } = entry;
    public string Name => Entry.Name;
    public string Category => string.IsNullOrWhiteSpace(Entry.Category) ? "未分类" : Entry.Category;
    public string TargetPath => Entry.TargetPath;
    public string TypeCaption => Entry.TargetType == TargetType.Shortcut ? "快捷方式" : "可执行程序";
    public bool Favorite => Entry.Favorite;
    public string FavoriteCaption => Favorite ? "取消收藏" : "收藏";
    public string FavoriteGlyph => Favorite ? "\uE735" : "\uE734";
    public bool HasCover => Entry.CoverId is not null;
    public string LastLaunchCaption => Entry.LastLaunchedAt is { } date ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "尚未启动";
    public string AddedCaption => Entry.AddedAt.ToLocalTime().ToString("yyyy-MM-dd");
    [ObservableProperty] private BitmapSource? image;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool missing;
    [ObservableProperty] private string availability = "检查路径…";
    [ObservableProperty] private bool launching;
    public string LaunchCaption => Launching ? "正在提交…" : "启动游戏";
    partial void OnLaunchingChanged(bool value) => OnPropertyChanged(nameof(LaunchCaption));

    public async Task LoadVisualAsync(CancellationToken token)
    {
        try
        {
            var exists = await Task.Run(() => File.Exists(TargetPath), token);
            token.ThrowIfCancellationRequested();
            Missing = !exists;
            Availability = exists ? TypeCaption : "路径失效 · 请重新定位";
            var loaded = await covers.GetAsync(Entry.CoverId, TargetPath, token);
            token.ThrowIfCancellationRequested();
            Image = loaded;
        }
        catch (OperationCanceledException) { }
    }

    public void RetainVisual() => visualUsers++;
    public void ReleaseVisual() { visualUsers = Math.Max(0, visualUsers - 1); if (visualUsers == 0) Image = null; }

    public void Update(GameEntry value)
    {
        var changedImage = Entry.CoverId != value.CoverId || Entry.TargetPath != value.TargetPath;
        Entry = value;
        if (changedImage) Image = null;
        foreach (var property in new[] { nameof(Name), nameof(Category), nameof(TargetPath), nameof(TypeCaption), nameof(Favorite),
            nameof(FavoriteCaption), nameof(FavoriteGlyph), nameof(HasCover), nameof(LastLaunchCaption), nameof(AddedCaption) }) OnPropertyChanged(property);
        if (changedImage) VisualChanged?.Invoke();
    }
}

public sealed record GameRow(IReadOnlyList<GameItemViewModel> Items, int Columns);
