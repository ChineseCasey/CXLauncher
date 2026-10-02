using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CXLauncher.Models;
using CXLauncher.Services;

namespace CXLauncher.ViewModels;

public partial class GameEditorViewModel(GameEntry original, bool isNew, CoverService covers) : ObservableObject
{
    public string Title => isNew ? "添加游戏" : "编辑游戏";
    public GameEntry? Result { get; private set; }
    public event Action? Saved;
    [ObservableProperty] private string name = original.Name;
    [ObservableProperty] private string targetPath = original.TargetPath;
    [ObservableProperty] private string arguments = original.Arguments;
    [ObservableProperty] private string workingDirectory = original.WorkingDirectory;
    [ObservableProperty] private string category = original.Category;
    [ObservableProperty] private bool favorite = original.Favorite;
    [ObservableProperty] private string coverFile = "";
    [ObservableProperty] private bool removeCover;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private string nameError = "";
    [ObservableProperty] private string pathError = "";
    [ObservableProperty] private string saveError = "";
    public bool IsExecutable => !Path.GetExtension(TargetPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    public bool CanSave => !IsSaving;
    public string CoverCaption => CoverFile.Length != 0 ? Path.GetFileName(CoverFile) : RemoveCover ? "不使用封面" : original.CoverId is not null ? "保留当前封面" : "未选择 · 将显示程序图标";

    partial void OnTargetPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsExecutable));
        if (!IsExecutable) { Arguments = ""; WorkingDirectory = ""; }
    }
    partial void OnCoverFileChanged(string value) { if (value.Length != 0) RemoveCover = false; OnPropertyChanged(nameof(CoverCaption)); }
    partial void OnRemoveCoverChanged(bool value) { if (value) CoverFile = ""; OnPropertyChanged(nameof(CoverCaption)); }
    partial void OnIsSavingChanged(bool value) { OnPropertyChanged(nameof(CanSave)); SaveCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        NameError = string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 200 ? "请输入 1–200 个字符的游戏名称。" : "";
        PathError = ""; SaveError = "";
        IsSaving = true;
        try
        {
            var game = original with
            {
                Name = Name.Trim(), TargetPath = TargetPath.Trim(),
                TargetType = IsExecutable ? TargetType.Executable : TargetType.Shortcut,
                Arguments = IsExecutable ? Arguments : "", WorkingDirectory = IsExecutable ? WorkingDirectory.Trim() : "",
                Category = string.IsNullOrWhiteSpace(Category) ? "未分类" : Category.Trim(), Favorite = Favorite,
                CoverId = RemoveCover ? null : original.CoverId
            };
            try { game.Validate(); }
            catch (InvalidDataException ex) { SaveError = ex.Message; return; }
            if (NameError.Length != 0) return;
            var exists = await Task.Run(() => File.Exists(game.TargetPath));
            if (!exists && (isNew || !game.TargetPath.Equals(original.TargetPath, StringComparison.OrdinalIgnoreCase)))
            { PathError = "文件不存在，请重新选择 .exe 或 .lnk 文件。"; return; }
            if (game.WorkingDirectory.Length != 0 && !await Task.Run(() => Directory.Exists(game.WorkingDirectory)))
            { SaveError = "工作目录不存在，请重新选择。"; return; }
            if (CoverFile.Length != 0) game = game with { CoverId = await covers.ImportAsync(CoverFile) };
            Result = game; Saved?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Windows.Markup.XamlParseException or FileFormatException)
        { SaveError = $"保存失败：{ex.Message}"; }
        finally { IsSaving = false; }
    }
}
