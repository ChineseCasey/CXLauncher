using System.Windows;
using System.Windows.Threading;
using CXLauncher.Services;
using CXLauncher.ViewModels;
using CXLauncher.Views;

namespace CXLauncher;

public partial class App : Application
{
    private SingleInstance? instance;
    private DiagnosticLog? log;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new AppPaths();
        log = new DiagnosticLog(paths);
        DispatcherUnhandledException += OnUnhandledException;
        instance = new SingleInstance(paths.Root);
        if (!instance.IsPrimary) { await instance.ActivatePrimaryAsync(); Shutdown(); return; }
        var covers = new CoverService(paths);
        var model = new MainViewModel(paths, covers, new GameLauncher(), new AppDialogs(covers), log);
        model.Ready += () => Dispatcher.BeginInvoke(() => log.Ready(model.TotalCount), DispatcherPriority.ApplicationIdle);
        MainWindow = new MainWindow(model);
        instance.Listen(() => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
            MainWindow.Show(); MainWindow.Activate(); MainWindow.Topmost = true; MainWindow.Topmost = false;
        }));
        MainWindow.Show();
    }
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        log?.Write("未处理错误", e.Exception);
        MessageBox.Show(MainWindow, "应用遇到意外错误，即将退出。已保存的游戏库会保留；诊断信息位于数据目录的 logs 文件夹。", "CX Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; Shutdown(1);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
