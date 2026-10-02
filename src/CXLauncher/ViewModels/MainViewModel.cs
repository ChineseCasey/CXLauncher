using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CXLauncher.Models;
using CXLauncher.Services;

namespace CXLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AtomicJsonStore<LibraryDocument> libraryStore;
    private readonly AtomicJsonStore<UserSettings> settingsStore;
    private readonly CoverService covers;
    private readonly GameLauncher launcher;
    private readonly IAppDialogs dialogs;
    private readonly DiagnosticLog log;
    private LibraryDocument library = new();
    private readonly List<GameItemViewModel> all = [];
    private CancellationTokenSource? searchCancellation;
    private int columns = 4;
    private bool initialized;
    public AppPaths Paths { get; }
    public UserSettings Settings { get; private set; } = new();
    public bool SettingsBlocked { get; private set; }
    public ObservableCollection<GameItemViewModel> VisibleGames { get; } = [];
    public ObservableCollection<GameRow> Rows { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["全部分类"];
    public string[] SortOptions { get; } = ["名称 A–Z", "最近启动", "最近添加"];
    public event Action<UserSettings>? SettingsLoaded;
    public event Action? Ready;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool libraryBlocked;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedCategory = "全部分类";
    [ObservableProperty] private int navigation;
    [ObservableProperty] private int sortMode;
    [ObservableProperty] private bool listView;
    [ObservableProperty] private GameItemViewModel? selectedGame;
    [ObservableProperty] private string notice = "选择 .exe 或 .lnk 文件，开始建立你的游戏库。";
    [ObservableProperty] private bool noticeIsError;

    public bool CanModify => initialized && !IsBusy && !LibraryBlocked;
    public bool CanTransfer => CanModify;
    public bool HasSelection => SelectedGame is not null;
    public bool HasGames => all.Count != 0;
    public bool HasResults => VisibleGames.Count != 0;
    public bool IsEmptyLibrary => initialized && !LibraryBlocked && !HasGames;
    public bool IsNoResults => initialized && !LibraryBlocked && HasGames && !HasResults;
    public bool CardView => !ListView;
    public int TotalCount => all.Count;
    public int FavoriteCount => all.Count(g => g.Favorite);
    public string ResultCaption => $"{VisibleGames.Count} 个游戏";
    public string PageTitle => Navigation switch { 1 => "最近启动", 2 => "我的收藏", _ => "游戏库" };
    public string PageSubtitle => Navigation switch { 1 => "每一次冒险，都有迹可循。", 2 => "喜欢的游戏，随时回到身边。", _ => "把喜欢的世界，放在一起。" };
    public string DataDirectory => Paths.Root;

    public MainViewModel(AppPaths paths, CoverService covers, GameLauncher launcher, IAppDialogs dialogs, DiagnosticLog log)
    {
        Paths = paths; this.covers = covers; this.launcher = launcher; this.dialogs = dialogs; this.log = log;
        libraryStore = new(Path.Combine(paths.Root, "library.json"), paths.Backups, () => new(), d => d.Validate());
        settingsStore = new(Path.Combine(paths.Root, "settings.json"), paths.Backups, () => new(), d => d.Validate());
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Run(Paths.EnsureDirectories);
            try { library = await libraryStore.LoadAsync(); ApplyLibrary(library); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LibraryBlocked = true; Report(ex, "读取游戏库"); }
            try
            {
                Settings = await settingsStore.LoadAsync();
                ListView = Settings.ListView; SortMode = Settings.SortMode;
                SettingsLoaded?.Invoke(Settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { SettingsBlocked = true; Report(ex, "读取设置"); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LibraryBlocked = true; SettingsBlocked = true; Report(ex, "准备数据目录"); }
        finally
        {
            initialized = true; IsBusy = false; Refresh();
            OnPropertyChanged(nameof(SettingsBlocked)); NotifyCommands();
            Ready?.Invoke();
        }
    }

    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanModify)); NotifyCommands(); }
    partial void OnLibraryBlockedChanged(bool value) { OnPropertyChanged(nameof(CanModify)); NotifyCommands(); }
    partial void OnNavigationChanged(int value)
    { OnPropertyChanged(nameof(PageTitle)); OnPropertyChanged(nameof(PageSubtitle)); Refresh(); }
    partial void OnSelectedCategoryChanged(string value) => Refresh();
    partial void OnSortModeChanged(int value) => Refresh();
    partial void OnListViewChanged(bool value) { OnPropertyChanged(nameof(CardView)); }
    partial void OnSelectedGameChanged(GameItemViewModel? oldValue, GameItemViewModel? newValue)
    { if (oldValue is not null) oldValue.IsSelected = false; if (newValue is not null) newValue.IsSelected = true; OnPropertyChanged(nameof(HasSelection)); }
    partial void OnSearchTextChanged(string value)
    {
        searchCancellation?.Cancel(); searchCancellation?.Dispose();
        searchCancellation = new();
        _ = DebounceSearchAsync(searchCancellation.Token);
    }
    private async Task DebounceSearchAsync(CancellationToken token)
    { try { await Task.Delay(60, token); Refresh(); } catch (OperationCanceledException) { } }

    private void NotifyCommands()
    {
        AddCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged(); LaunchCommand.NotifyCanExecuteChanged(); ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged(); RecoverLibraryCommand.NotifyCanExecuteChanged(); NewLibraryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand] private void Navigate(string? section) { if (int.TryParse(section, out var index)) Navigation = index; }
    [RelayCommand] private void Select(GameItemViewModel? game) => SelectedGame = game;
    [RelayCommand] private void CloseDetails() => SelectedGame = null;
    [RelayCommand] private void ClearSearch() { SearchText = ""; SelectedCategory = "全部分类"; }
    [RelayCommand] private void ShowDataSettings() => dialogs.ShowDataSettings(this);

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task AddAsync()
    { foreach (var path in dialogs.ChooseGames()) await AddPathAsync(path); }

    public async Task AddPathAsync(string path)
    {
        if (!CanModify) return;
        if (!new[] { ".exe", ".lnk" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        { SetNotice("仅支持拖入 .exe 和 .lnk 文件。", true); return; }
        if (library.Games.Any(g => string.Equals(Path.GetFullPath(g.TargetPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
        { SetNotice("这个路径已经在游戏库中。", true); return; }
        await RunAsync(async () =>
        {
            var result = await dialogs.EditAsync(GameEntry.FromPath(path), true);
            if (result is null) return;
            if (library.Games.Any(g => string.Equals(Path.GetFullPath(g.TargetPath), Path.GetFullPath(result.TargetPath), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("这个路径已经在游戏库中。");
            await CommitAsync(new LibraryDocument { Games = [.. library.Games, result] });
            SetNotice($"已添加「{result.Name}」。");
        }, "添加游戏");
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task EditAsync(GameItemViewModel? game)
    {
        game ??= SelectedGame; if (game is null) return;
        await RunAsync(async () =>
        {
            var result = await dialogs.EditAsync(game.Entry, false); if (result is null) return;
            if (library.Games.Any(g => g.Id != result.Id && string.Equals(Path.GetFullPath(g.TargetPath), Path.GetFullPath(result.TargetPath), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("这个路径已被另一个游戏使用。");
            await CommitAsync(new LibraryDocument { Games = library.Games.Select(g => g.Id == result.Id ? result : g).ToList() });
            SetNotice($"已保存「{result.Name}」。");
        }, "编辑游戏");
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task RemoveAsync(GameItemViewModel? game)
    {
        game ??= SelectedGame; if (game is null || !dialogs.Confirm("移出游戏库", $"将「{game.Name}」移出游戏库？游戏文件会保留。")) return;
        await RunAsync(async () =>
        {
            await CommitAsync(new LibraryDocument { Games = library.Games.Where(g => g.Id != game.Entry.Id).ToList() });
            SetNotice($"已移除「{game.Name}」，游戏文件已保留。");
        }, "移除游戏");
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task ToggleFavoriteAsync(GameItemViewModel? game)
    {
        if (game is null) return;
        await RunAsync(async () =>
        {
            await CommitAsync(new LibraryDocument { Games = library.Games.Select(g => g.Id == game.Entry.Id ? g with { Favorite = !g.Favorite } : g).ToList() });
            SetNotice(game.Favorite ? "已加入收藏。" : "已取消收藏。");
        }, "更新收藏");
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task LaunchAsync(GameItemViewModel? game)
    {
        game ??= SelectedGame; if (game is null) return;
        await RunAsync(async () =>
        {
            game.Launching = true;
            try
            {
                var result = await launcher.LaunchAsync(game.Entry);
                SetNotice(result.Message, !result.Success);
                if (!result.Success) { await game.LoadVisualAsync(CancellationToken.None); return; }
                var updated = game.Entry with { LastLaunchedAt = result.SubmittedAt };
                try { await CommitAsync(new LibraryDocument { Games = library.Games.Select(g => g.Id == updated.Id ? updated : g).ToList() }); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { log.Write("记录启动时间", ex); SetNotice("启动请求已提交，但最近启动时间保存失败。请检查数据目录权限。", true); }
            }
            finally { game.Launching = false; }
        }, "启动游戏");
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task ImportAsync()
    {
        var path = dialogs.ChooseImport(); if (path is null) return;
        await RunAsync(async () =>
        {
            var preview = LibraryTransfer.Preview(library, await LibraryTransfer.ReadAsync(path));
            if (!dialogs.Confirm("确认合并游戏库", $"将新增 {preview.Added} 个游戏，跳过 {preview.Skipped} 个已有路径。\n\n导入仅合并条目，不会启动游戏。JSON 不包含图片，{preview.MissingCovers} 个封面需要重新选择。")) return;
            await CommitAsync(preview.Merged); SetNotice($"导入完成：新增 {preview.Added} 个，跳过 {preview.Skipped} 个。");
        }, "导入游戏库");
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task ExportAsync()
    {
        var path = dialogs.ChooseExport(); if (path is null) return;
        await RunAsync(async () =>
        {
            if (Path.GetFullPath(path).StartsWith(Paths.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择应用数据目录之外的位置，避免覆盖正在使用的配置。");
            await LibraryTransfer.ExportAsync(library, path); SetNotice("游戏库已导出。封面请另行备份 covers 文件夹。");
        }, "导出游戏库");
    }

    private bool CanRecover => LibraryBlocked && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task RecoverLibraryAsync()
    {
        await RunAsync(async () =>
        {
            var backup = await libraryStore.FindLatestBackupAsync();
            if (backup is null) { SetNotice("没有找到有效备份。可保留损坏文件后新建游戏库。", true); return; }
            if (!dialogs.Confirm("恢复有效备份", $"备份包含 {backup.Games.Count} 个游戏。原配置会保留为 .preserved 文件，确认恢复？")) return;
            await libraryStore.RecoverAsync(backup); LibraryBlocked = false; ApplyLibrary(backup); SetNotice("已从最近有效备份恢复游戏库，原配置已保留。");
        }, "恢复游戏库");
    }

    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task NewLibraryAsync()
    {
        if (!dialogs.Confirm("保留原文件并新建", "原配置将保留为 .preserved 文件，新游戏库从空库开始。之后可以通过导入找回条目，确认继续？")) return;
        await RunAsync(async () => { var empty = new LibraryDocument(); await libraryStore.RecoverAsync(empty); LibraryBlocked = false; ApplyLibrary(empty); SetNotice("已新建游戏库，原配置已保留。"); }, "新建游戏库");
    }

    [RelayCommand]
    private async Task RecoverSettingsAsync()
    {
        if (!SettingsBlocked || IsBusy || !dialogs.Confirm("恢复设置", "优先恢复最近有效设置；没有有效备份时使用默认设置。原文件会保留，确认继续？")) return;
        await RunAsync(async () =>
        {
            Settings = await settingsStore.FindLatestBackupAsync() ?? new UserSettings();
            await settingsStore.RecoverAsync(Settings); SettingsBlocked = false;
            ListView = Settings.ListView; SortMode = Settings.SortMode; SettingsLoaded?.Invoke(Settings);
            OnPropertyChanged(nameof(SettingsBlocked)); SetNotice("设置已恢复，原文件已保留。");
        }, "恢复设置");
    }

    public async Task<bool> SaveSettingsAsync(UserSettings windowSettings)
    {
        if (SettingsBlocked) return true;
        try
        {
            Settings = windowSettings with { ListView = ListView, SortMode = SortMode };
            await settingsStore.SaveAsync(Settings); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Report(ex, "保存设置"); return false; }
    }

    private async Task RunAsync(Func<Task> action, string operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
        { Report(ex, operation); }
        finally { IsBusy = false; Refresh(); }
    }
    private async Task CommitAsync(LibraryDocument document)
    {
        try { await libraryStore.SaveAsync(document); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            LibraryBlocked = true;
            throw new DataFileException(libraryStore.FilePath, "游戏库配置已损坏或版本不支持，原文件已保留，请先恢复游戏库。", ex);
        }
        ApplyLibrary(document);
    }
    private void ApplyLibrary(LibraryDocument document)
    {
        var old = all.ToDictionary(g => g.Entry.Id);
        all.Clear(); library = document;
        foreach (var game in library.Games)
        {
            if (old.TryGetValue(game.Id, out var item)) item.Update(game);
            else item = new(game, covers);
            all.Add(item);
        }
        if (SelectedGame is { } selected && !all.Contains(selected)) SelectedGame = null;
        var category = SelectedCategory;
        Categories.Clear(); Categories.Add("全部分类");
        foreach (var name in all.Select(g => g.Category).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)) Categories.Add(name);
        SelectedCategory = Categories.Contains(category) ? category : "全部分类";
        OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(FavoriteCount)); Refresh();
    }

    public void SetGridWidth(double width)
    { var count = Math.Clamp((int)(width / 204), 1, 8); if (count != columns) { columns = count; RebuildRows(); } }

    public void Refresh()
    {
        IEnumerable<GameItemViewModel> query = all;
        if (Navigation == 1) query = query.Where(g => g.Entry.LastLaunchedAt is not null);
        if (Navigation == 2) query = query.Where(g => g.Favorite);
        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "全部分类") query = query.Where(g => g.Category == SelectedCategory);
        var term = SearchText.Trim();
        if (term.Length != 0) query = query.Where(g => g.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        query = (Navigation == 1 ? 1 : SortMode) switch
        {
            1 => query.OrderByDescending(g => g.Entry.LastLaunchedAt).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => query.OrderByDescending(g => g.Entry.AddedAt).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        var result = query.ToArray();
        if (!VisibleGames.SequenceEqual(result))
        { VisibleGames.Clear(); foreach (var game in result) VisibleGames.Add(game); RebuildRows(); }
        foreach (var property in new[] { nameof(HasGames), nameof(HasResults), nameof(IsEmptyLibrary), nameof(IsNoResults), nameof(ResultCaption) }) OnPropertyChanged(property);
    }
    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var chunk in VisibleGames.Chunk(columns)) Rows.Add(new(chunk, columns));
    }
    private void Report(Exception error, string operation) { log.Write(operation, error); SetNotice($"{operation}失败：{error.Message}", true); }
    public void SetNotice(string text, bool error = false) { Notice = text; NoticeIsError = error; }
}
