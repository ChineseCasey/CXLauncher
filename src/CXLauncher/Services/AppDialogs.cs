using System.Windows;
using CXLauncher.Models;
using CXLauncher.ViewModels;
using CXLauncher.Views;
using Microsoft.Win32;

namespace CXLauncher.Services;

public sealed class AppDialogs(CoverService covers) : IAppDialogs
{
    private static Window Owner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
    public string[] ChooseGames()
    {
        var picker = new OpenFileDialog { Title = "选择本地游戏", Filter = "游戏程序与快捷方式|*.exe;*.lnk", Multiselect = true, DereferenceLinks = false };
        return picker.ShowDialog(Owner) == true ? picker.FileNames : [];
    }
    public string? ChooseImport()
    {
        var picker = new OpenFileDialog { Title = "导入游戏库", Filter = "游戏库 JSON|*.json", DereferenceLinks = false };
        return picker.ShowDialog(Owner) == true ? picker.FileName : null;
    }
    public string? ChooseExport()
    {
        var picker = new SaveFileDialog { Title = "导出游戏库", Filter = "游戏库 JSON|*.json", FileName = "CXLauncher-library.json", AddExtension = true };
        return picker.ShowDialog(Owner) == true ? picker.FileName : null;
    }
    public bool Confirm(string title, string message) => MessageBox.Show(Owner, message, title,
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public Task<GameEntry?> EditAsync(GameEntry entry, bool isNew)
    {
        var model = new GameEditorViewModel(entry, isNew, covers);
        var window = new GameEditorWindow(model) { Owner = Owner };
        return Task.FromResult(window.ShowDialog() == true ? model.Result : null);
    }
    public void ShowDataSettings(MainViewModel viewModel) => new DataSettingsWindow { Owner = Owner, DataContext = viewModel }.ShowDialog();
}
