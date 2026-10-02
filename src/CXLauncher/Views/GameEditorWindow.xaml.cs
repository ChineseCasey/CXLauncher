using System.Windows;
using CXLauncher.ViewModels;
using Microsoft.Win32;

namespace CXLauncher.Views;

public partial class GameEditorWindow : Window
{
    private readonly GameEditorViewModel model;
    public GameEditorWindow(GameEditorViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        model.Saved += () => DialogResult = true;
        Loaded += (_, _) => { NativeWindow.UseDarkCaption(this); Motion.Reveal((System.Windows.FrameworkElement)Content, y: 8); NameInput.Focus(); NameInput.SelectAll(); };
        Closing += (_, e) => { if (model.IsSaving && model.Result is null) e.Cancel = true; };
    }
    private void BrowseTarget(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "游戏程序与快捷方式|*.exe;*.lnk", DereferenceLinks = false };
        if (picker.ShowDialog(this) == true) model.TargetPath = picker.FileName;
    }
    private void BrowseCover(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "本地图片|*.png;*.jpg;*.jpeg;*.bmp", DereferenceLinks = false };
        if (picker.ShowDialog(this) == true) model.CoverFile = picker.FileName;
    }
    private void BrowseDirectory(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "选择工作目录" };
        if (picker.ShowDialog(this) == true) model.WorkingDirectory = picker.FolderName;
    }
}
