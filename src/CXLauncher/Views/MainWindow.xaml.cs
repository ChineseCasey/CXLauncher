using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CXLauncher.Models;
using CXLauncher.ViewModels;

namespace CXLauncher.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private Window? detailDialog;
    private bool allowClose;
    private bool closingInProgress;
    private bool ready;
    private bool revealPending;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        model.SettingsLoaded += RestoreWindow;
        model.PropertyChanged += OnModelChanged;
        SizeChanged += (_, _) => UpdateDetails(false);
        Loaded += async (_, _) =>
        {
            NativeWindow.UseDarkCaption(this);
            Motion.Reveal(PageHeader, duration: 160);
            Motion.Reveal(SearchToolbar, duration: 160, delay: 25);
            await model.InitializeAsync(); ready = true;
        };
        Closing += OnClosing;
        Closed += (_, _) => { model.SettingsLoaded -= RestoreWindow; model.PropertyChanged -= OnModelChanged; Motion.CancelAll(this); };
        var focusSearch = new RoutedCommand();
        InputBindings.Add(new KeyBinding(focusSearch, Key.F, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(focusSearch, (_, _) => { SearchBox.Focus(); SearchBox.SelectAll(); }));
    }
    private void RestoreWindow(UserSettings settings)
    {
        Width = settings.Width; Height = settings.Height;
        // Virtual-screen bounds also permit monitors to the left/above the primary display.
        if (settings.Left is { } left && settings.Top is { } top && left + Width > SystemParameters.VirtualScreenLeft + 80 &&
            top + Height > SystemParameters.VirtualScreenTop + 80 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
        { Left = left; Top = top; }
        if (settings.Maximized) WindowState = WindowState.Maximized;
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedGame)) UpdateDetails(true);
        if (!ready) return;
        if (e.PropertyName == nameof(MainViewModel.Notice)) Motion.Fade(NoticeSurface);
        if (e.PropertyName is nameof(MainViewModel.Navigation) or nameof(MainViewModel.ListView) or nameof(MainViewModel.SelectedCategory))
        {
            if (e.PropertyName == nameof(MainViewModel.Navigation)) Motion.Reveal(PageHeader, duration: 140);
            QueueResultsReveal();
        }
    }
    private void QueueResultsReveal()
    {
        if (revealPending) return;
        revealPending = true;
        Dispatcher.InvokeAsync(() => { revealPending = false; if (IsVisible) Motion.Reveal(ResultsArea, y: 6, duration: 160); }, System.Windows.Threading.DispatcherPriority.Render);
    }
    private void UpdateDetails(bool selectionChanged)
    {
        if (model is null) return;
        var show = model.HasSelection && ActualWidth >= 1160;
        var wasVisible = DetailsPane.IsVisible;
        DetailsColumn.Width = new GridLength(show ? 316 : 0);
        DetailsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && (selectionChanged || !wasVisible)) DetailsPane.RevealSelection();
        if (!model.HasSelection) { detailDialog?.Close(); return; }
        if (!show && selectionChanged && detailDialog is null)
        {
            detailDialog = new Window { Owner = this, Title = "游戏详情", Width = 440, Height = 720, MinWidth = 400, MinHeight = 480,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new GameDetails { DataContext = model, Margin = new Thickness(16) } };
            detailDialog.InputBindings.Add(new KeyBinding(model.CloseDetailsCommand, Key.Escape, ModifierKeys.None));
            detailDialog.Loaded += (_, _) => { NativeWindow.UseDarkCaption(detailDialog); ((GameDetails)detailDialog.Content).RevealSelection(); };
            detailDialog.Closed += (_, _) => { detailDialog = null; model.CloseDetailsCommand.Execute(null); };
            detailDialog.ShowDialog();
        }
    }
    private void ToggleView(object sender, RoutedEventArgs e) => model.ListView = !model.ListView;
    private void OnResultsSizeChanged(object sender, SizeChangedEventArgs e) => model?.SetGridWidth(e.NewSize.Width - 12);
    private void OnDragOver(object sender, DragEventArgs e)
    {
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        e.Effects = model.CanModify && paths is not null && paths.Any(IsGamePath) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private static bool IsGamePath(string path) => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    private async void OnDrop(object sender, DragEventArgs e)
    { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) foreach (var path in paths.Where(IsGamePath)) await model.AddPathAsync(path); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closingInProgress) return;
        if (model.IsBusy) { model.SetNotice("正在完成操作，请稍候再关闭窗口。"); return; }
        closingInProgress = true;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var settings = new UserSettings { Width = Math.Max(860, bounds.Width), Height = Math.Max(560, bounds.Height), Left = bounds.Left, Top = bounds.Top, Maximized = WindowState == WindowState.Maximized };
        var saved = await model.SaveSettingsAsync(settings);
        if (!saved && MessageBox.Show(this, "窗口设置保存失败，是否仍然退出？游戏库已完成的保存不受影响。", "退出 CX Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        { closingInProgress = false; return; }
        allowClose = true; Close();
    }
}
