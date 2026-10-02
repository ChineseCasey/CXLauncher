using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using CXLauncher.Models;
using CXLauncher.Services;
using CXLauncher.ViewModels;
using CXLauncher.Views;

namespace CXLauncher.Tests;

internal static class Program
{
    private static int passed;
    private static string root = "";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--probe")
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new ProbeResult(args.Skip(2).ToArray(), Environment.CurrentDirectory, Environment.ProcessId)));
            if (args.Contains("--linger")) Thread.Sleep(1500);
            return 0;
        }
        root = Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("CXLAUNCHER_TEST_ROOT") ?? Path.Combine(Environment.CurrentDirectory, ".artifacts", "tests")), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CoreAsync().GetAwaiter().GetResult();
            if (args.Contains("--ui-smoke")) RunUiSmoke();
            Console.WriteLine($"PASS: {passed} checks. Artifacts: {root}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    { try { await action(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException("FAIL: " + name); }
    private static GameEntry Game(string name = "中文游戏") => GameEntry.FromPath(Path.Combine(root, name + ".exe"));

    private static async Task CoreAsync()
    {
        FluidFieldChecks();
        var paths = new AppPaths(Path.Combine(root, "store")); paths.EnsureDirectories();
        var store = new AtomicJsonStore<LibraryDocument>(Path.Combine(paths.Root, "library.json"), paths.Backups, () => new(), d => d.Validate());
        Check((await store.LoadAsync()).Games.Count == 0 && !File.Exists(store.FilePath), "absent file loads defaults without writing");
        var game = Game("中文 带空格 & (测试)") with { Arguments = "--name \"中文 参数\" --literal \"&()$;\"", Favorite = true, Category = "动作" };
        var document = new LibraryDocument { Games = [game] };
        await store.SaveAsync(document);
        var reloaded = await store.LoadAsync();
        Check(reloaded.Games.Single() == game, "all fields survive JSON round trip");
        var json = await File.ReadAllTextAsync(store.FilePath);
        Check(json.Contains("schemaVersion") && json.Contains("targetPath") && json.Contains("Executable"), "versioned camelCase schema");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => store.SaveAsync(new LibraryDocument { Games = [game with { Name = "Game " + i }] })));
        Check((await store.LoadAsync()).Games.Count == 1 && Directory.GetFiles(paths.Backups, "*.bak").Length == 5, "concurrent writes serialized; five backups retained");
        Check(Directory.GetFiles(paths.Root, "*.tmp").Length == 0, "atomic writes leave no temporary files");
        await ThrowsAsync<InvalidDataException>(() => store.SaveAsync(document with { SchemaVersion = 9 }), "unsupported schema rejected before write");
        await ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new LibraryDocument { Games = [game, game] }), "duplicate identifiers rejected");
        await ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new LibraryDocument { Games = [game with { TargetPath = "relative.exe" }] }), "relative targets rejected");
        await ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new LibraryDocument { Games = [game with { CoverId = "../../private" }] }), "cover traversal rejected");
        await File.WriteAllTextAsync(store.FilePath, "{broken-json");
        await ThrowsAsync<DataFileException>(async () => _ = await store.LoadAsync(), "corrupt file detected");
        await ThrowsAsync<JsonException>(() => store.SaveAsync(new LibraryDocument()), "corrupt file cannot be replaced by defaults");
        Check(await File.ReadAllTextAsync(store.FilePath) == "{broken-json", "corrupt original untouched after rejected save");
        var backup = await store.FindLatestBackupAsync();
        Check(backup is not null && backup.Games.Count == 1, "latest valid backup discovered");
        await store.RecoverAsync(backup!);
        Check((await store.LoadAsync()).Games.Count == 1 && Directory.GetFiles(paths.Root, "*.preserved-*").Any(p => File.ReadAllText(p) == "{broken-json"), "recovery restores library and preserves damaged original");
        await File.WriteAllTextAsync(store.FilePath, "{\"schemaVersion\":99,\"games\":[]}");
        await ThrowsAsync<DataFileException>(async () => _ = await store.LoadAsync(), "newer schema blocks library loading");
        await ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new LibraryDocument()), "newer schema cannot be overwritten");

        var imported = new LibraryDocument { Games = [game with { Id = Guid.NewGuid() }, Game("第二个") with { Id = game.Id, CoverId = Guid.NewGuid().ToString("N") }] };
        var preview = LibraryTransfer.Preview(document, imported);
        Check(preview.Added == 1 && preview.Skipped == 1 && preview.MissingCovers == 1 && preview.Merged.Games.Last().Id != game.Id, "import previews duplicates and remaps colliding IDs");
        Check(preview.Merged.Games.Last().CoverId is null && preview.Merged.Games.Last().LastLaunchedAt is null, "import drops foreign covers without launching");
        var incomplete = Path.Combine(root, "incomplete.json"); await File.WriteAllTextAsync(incomplete, "{}");
        await ThrowsAsync<JsonException>(async () => _ = await LibraryTransfer.ReadAsync(incomplete), "missing required schema rejected instead of interpreted as empty library");
        var exported = Path.Combine(root, "export.json");
        await LibraryTransfer.ExportAsync(document, exported);
        Check((await LibraryTransfer.ReadAsync(exported)).Games.Single() == game, "export/import preserves game metadata");
        var shortcut = GameEntry.FromPath(Path.Combine(root, "快捷方式.lnk"));
        var info = GameLauncher.CreateStartInfo(shortcut);
        Check(info.UseShellExecute && info.Arguments.Length == 0 && info.WorkingDirectory.Length == 0, "shortcut uses shell semantics without override");
        await ThrowsAsync<InvalidDataException>(() => Task.Run(() => GameLauncher.CreateStartInfo(shortcut with { Arguments = "injected" })), "shortcut argument overrides forbidden");
        var exeInfo = GameLauncher.CreateStartInfo(game);
        Check(!exeInfo.UseShellExecute && exeInfo.FileName == game.TargetPath && exeInfo.Arguments == game.Arguments && exeInfo.WorkingDirectory == Path.GetDirectoryName(game.TargetPath), "exe target, arguments and working directory passed separately");
        Check(!(await new GameLauncher().LaunchAsync(Game("missing"))).Success, "missing executable reports failure");
        await LaunchIntegrationAsync();
        await CoverChecksAsync(paths);
        await ViewModelChecksAsync();
    }

    private static async Task LaunchIntegrationAsync()
    {
        var directory = Path.Combine(root, "中文 路径 & (游戏)"); Directory.CreateDirectory(directory);
        foreach (var source in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), true);
        var executable = Path.Combine(directory, "中文 游戏 & (测试).exe");
        File.Copy(Path.Combine(directory, "CXLauncher.Tests.exe"), executable, true);
        var output = Path.Combine(root, "probe output 中文.json");
        var working = Path.Combine(root, "工作 目录 & (custom)"); Directory.CreateDirectory(working);
        var target = GameEntry.FromPath(executable) with { Arguments = $"--probe \"{output}\" \"中文 参数\" \"&()$;\" --linger", WorkingDirectory = working };
        var launcher = new GameLauncher();
        var result = await launcher.LaunchAsync(target);
        Check(result.Success && result.SubmittedAt is not null, "real process launch accepted");
        Check(!(await launcher.LaunchAsync(target)).Success, "duplicate click suppressed");
        await WaitForFileAsync(output);
        var captured = JsonSerializer.Deserialize<ProbeResult>(await File.ReadAllTextAsync(output))!;
        Check(captured.Arguments.SequenceEqual(new[] { "中文 参数", "&()$;", "--linger" }) && captured.WorkingDirectory == working, "real process receives Unicode, spaces, literal metacharacters and custom working directory");
        Check(!Process.GetProcessById(captured.ProcessId).HasExited, "launcher does not terminate child after submission");
        var badWorking = target with { Id = Guid.NewGuid(), WorkingDirectory = Path.Combine(root, "not-here") };
        Check(!(await launcher.LaunchAsync(badWorking)).Success, "invalid working directory returns actionable failure");
        // Create a real Windows Shell shortcut using WSH. The shortcut supplies its own arguments and working directory.
        var shortcutPath = Path.Combine(root, "真实 快捷方式.lnk");
        var shortcutOutput = Path.Combine(root, "shortcut output.json");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(shortcutPath);
        try
        {
            link.TargetPath = executable;
            link.Arguments = $"--probe \"{shortcutOutput}\" \"shortcut 参数\"";
            link.WorkingDirectory = working;
            link.Save();
        }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        Check((await new GameLauncher().LaunchAsync(GameEntry.FromPath(shortcutPath))).Success, "real .lnk submitted via Windows Shell");
        await WaitForFileAsync(shortcutOutput);
        var shortcutCaptured = JsonSerializer.Deserialize<ProbeResult>(await File.ReadAllTextAsync(shortcutOutput))!;
        Check(shortcutCaptured.WorkingDirectory == working && shortcutCaptured.Arguments.SequenceEqual(new[] { "shortcut 参数" }), "shortcut preserves its embedded arguments and directory");
    }
    private static async Task WaitForFileAsync(string path)
    {
        for (var i = 0; i < 100; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Probe did not write " + path);
    }

    private static async Task CoverChecksAsync(AppPaths paths)
    {
        var image = BitmapSource.Create(120, 180, 96, 96, PixelFormats.Bgr32, null, new byte[120 * 180 * 4], 120 * 4);
        image.Freeze();
        var original = Path.Combine(root, "cover.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(original)) encoder.Save(file);
        var service = new CoverService(paths);
        var id = await service.ImportAsync(original); File.Delete(original);
        var thumbnail = await service.GetAsync(id, "", CancellationToken.None);
        Check(thumbnail is { IsFrozen: true } && thumbnail.PixelWidth <= 256 && File.Exists(paths.CoverPath(id)), "cover copied independently and decoded into frozen thumbnail");
        Check(File.Exists(Path.Combine(paths.Thumbnails, id + ".png")), "thumbnail disk cache generated");
        var canceled = new CancellationToken(true);
        await ThrowsAsync<OperationCanceledException>(async () => _ = await service.GetAsync(Guid.NewGuid().ToString("N"), "", canceled), "offscreen pending decode can be canceled");
        for (var i = 0; i < 70; i++)
        {
            var next = Guid.NewGuid().ToString("N"); File.Copy(paths.CoverPath(id)!, paths.CoverPath(next)!);
            _ = await service.GetAsync(next, "", CancellationToken.None);
        }
        Check(service.CachedImageCount == 64, "image memory cache bounded at 64 entries");
    }

    private static async Task ViewModelChecksAsync()
    {
        var paths = new AppPaths(Path.Combine(root, "view-model")); paths.EnsureDirectories();
        var store = new AtomicJsonStore<LibraryDocument>(Path.Combine(paths.Root, "library.json"), paths.Backups, () => new(), d => d.Validate());
        var entries = new[] { Game("Alpha") with { Favorite = true, Category = "动作" }, Game("中文冒险") with { Category = "冒险", LastLaunchedAt = DateTimeOffset.UtcNow }, Game("Zeta") };
        await store.SaveAsync(new LibraryDocument { Games = entries.ToList() });
        var dialogs = new TestDialogs();
        var model = new MainViewModel(paths, new CoverService(paths), new GameLauncher(), dialogs, new DiagnosticLog(paths));
        await model.InitializeAsync();
        Check(model.TotalCount == 3 && model.CanModify && model.VisibleGames[0].Name == entries.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).First().Name, "view model restores saved library with culture-aware name sorting");
        model.Navigation = 2; Check(model.VisibleGames.Count == 1, "favorites navigation filters games");
        model.Navigation = 1; Check(model.VisibleGames.Single().Name == "中文冒险", "recent page only contains successfully submitted games");
        model.Navigation = 0; model.SearchText = "中文"; model.Refresh(); Check(model.VisibleGames.Single().Name == "中文冒险", "Unicode name search");
        model.SearchText = ""; model.SelectedCategory = "动作"; Check(model.VisibleGames.Single().Name == "Alpha", "category filtering");
        model.SelectedCategory = "全部分类";
        var item = model.VisibleGames.Single(g => g.Name == "Alpha");
        await model.ToggleFavoriteCommand.ExecuteAsync(item);
        Check(!item.Favorite && !(await store.LoadAsync()).Games.Single(g => g.Id == item.Entry.Id).Favorite, "favorite commit updates memory and persisted data");
        await model.RemoveCommand.ExecuteAsync(item);
        Check(model.TotalCount == 2 && dialogs.ConfirmationCount == 1, "removal requires confirmation and commits library only");
        await File.WriteAllTextAsync(store.FilePath, "broken");
        var remaining = model.VisibleGames[0]; var before = remaining.Favorite;
        await model.ToggleFavoriteCommand.ExecuteAsync(remaining);
        Check(remaining.Favorite == before && model.NoticeIsError && model.LibraryBlocked && await File.ReadAllTextAsync(store.FilePath) == "broken", "failed commit preserves memory and corruption, and enables recovery");
        var blocked = new MainViewModel(paths, new CoverService(paths), new GameLauncher(), dialogs, new DiagnosticLog(paths));
        await blocked.InitializeAsync();
        Check(blocked.LibraryBlocked && !blocked.CanModify && blocked.RecoverLibraryCommand.CanExecute(null), "corrupt startup blocks mutations and exposes recovery");
        await blocked.RecoverLibraryCommand.ExecuteAsync(null);
        Check(!blocked.LibraryBlocked && blocked.CanModify && blocked.TotalCount > 0, "confirmed backup recovery reenables library");
        await model.SaveSettingsAsync(new UserSettings { Width = 1000, Height = 700 });
        var settings = new AtomicJsonStore<UserSettings>(Path.Combine(paths.Root, "settings.json"), paths.Backups, () => new(), s => s.Validate());
        Check((await settings.LoadAsync()).Width == 1000, "window preferences persist");
        var editor = new GameEditorViewModel(Game("Editor"), true, new CoverService(paths)) { Name = "" };
        await editor.SaveCommand.ExecuteAsync(null);
        Check(editor.Result is null && editor.NameError.Length != 0, "editor retains invalid input with inline name error");
        var fixture = Game("添加与编辑 fixture"); await File.WriteAllTextAsync(fixture.TargetPath, "test fixture");
        dialogs.EditResult = fixture;
        await blocked.AddPathAsync(fixture.TargetPath);
        Check((await store.LoadAsync()).Games.Any(g => g.Id == fixture.Id), "confirmed add persists selected target");
        var count = blocked.TotalCount;
        await blocked.AddPathAsync(fixture.TargetPath);
        Check(blocked.TotalCount == count && blocked.NoticeIsError, "duplicate path add rejected without duplicate record");
        var added = blocked.VisibleGames.Single(g => g.Entry.Id == fixture.Id);
        dialogs.EditResult = added.Entry with { Name = "重命名 中文游戏", Arguments = "--windowed", Category = "测试" };
        await blocked.EditCommand.ExecuteAsync(added);
        Check((await store.LoadAsync()).Games.Single(g => g.Id == fixture.Id).Name == "重命名 中文游戏" && added.Entry.Arguments == "--windowed", "edit persists name, arguments and category");
        var restarted = new MainViewModel(paths, new CoverService(paths), new GameLauncher(), dialogs, new DiagnosticLog(paths));
        await restarted.InitializeAsync();
        Check(restarted.VisibleGames.Single(g => g.Entry.Id == fixture.Id).Name == "重命名 中文游戏", "restart restores added and edited game");
        await blocked.RemoveCommand.ExecuteAsync(added);
        Check(File.Exists(fixture.TargetPath) && !(await store.LoadAsync()).Games.Any(g => g.Id == fixture.Id), "removing an existing target keeps the physical file");
    }

    private static void RunUiSmoke()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/CXLauncher;component/Themes/Dark.xaml", UriKind.Relative) });
        var paths = new AppPaths(Path.Combine(root, "ui")); paths.EnsureDirectories();
        var dialogs = new TestDialogs();
        var model = new MainViewModel(paths, new CoverService(paths), new GameLauncher(), dialogs, new DiagnosticLog(paths));
        var window = new MainWindow(model) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        app.MainWindow = window;
        Exception? failure = null;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await WaitUntilAsync(() => model.CanModify);
                window.UpdateLayout(); Capture(window, "empty-1180.png");
                Check(model.IsEmptyLibrary && window.FindName("SearchBox") is TextBox, "empty WPF window initializes without XAML or binding failure");
                var primary = Descendants<Button>(window).Single(b => b.Content is string text && text == "添加第一个游戏");
                var primaryText = Descendants<TextBlock>(primary).First();
                Check(((SolidColorBrush)primaryText.Foreground).Color == ((SolidColorBrush)window.FindResource("OnAccentBrush")).Color, "primary button text inherits accessible dark foreground");
                var icons = new[] { "BrandIcon", "LibraryIcon", "RecentIcon", "FavoriteIcon" }.Select(key => (DrawingImage)window.FindResource(key)).ToArray();
                Check(icons.All(icon => icon.IsFrozen && icon.Width == 24 && icon.Height == 24), "monochrome vector icon resources are frozen and retain consistent 24-unit bounds");
                var focusSearch = window.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.F && b.Modifiers == ModifierKeys.Control);
                ((RoutedCommand)focusSearch.Command).Execute(null, window);
                Check(Keyboard.FocusedElement == window.FindName("SearchBox"), "Ctrl+F command focuses search field");
                await MotionChecksAsync(window, primary);
                await LiquidChecksAsync(window);
                var store = new AtomicJsonStore<LibraryDocument>(Path.Combine(paths.Root, "library.json"), paths.Backups, () => new(), d => d.Validate());
                var games = Enumerable.Range(0, 1000).Select(i => Game("游戏 " + i.ToString("D4")) with { Category = i % 2 == 0 ? "动作" : "冒险", Favorite = i % 7 == 0 }).ToList();
                // Use a real imported library so the UI is exercised through its public command.
                dialogs.ImportPath = Path.Combine(root, "ui-import.json");
                await LibraryTransfer.ExportAsync(new LibraryDocument { Games = games }, dialogs.ImportPath);
                await model.ImportCommand.ExecuteAsync(null);
                await Task.Delay(500); window.UpdateLayout(); Capture(window, "library-1000.png");
                var rows = (ListBox)window.FindName("CardRows");
                var realized = Enumerable.Range(0, rows.Items.Count).Count(i => rows.ItemContainerGenerator.ContainerFromIndex(i) is not null);
                Check(model.TotalCount == 1000 && realized > 0 && realized < 12, $"card rows virtualized: {realized}/{rows.Items.Count} realized for 1000 games");
                await GlassChecksAsync(window, rows);
                rows.ScrollIntoView(rows.Items[^1]); await Task.Delay(250); window.UpdateLayout();
                var scrolledRealized = Enumerable.Range(0, rows.Items.Count).Count(i => rows.ItemContainerGenerator.ContainerFromIndex(i) is not null);
                Check(scrolledRealized < 12, $"card row recycling while scrolling: {scrolledRealized} realized");
                var sourceCover = Directory.GetFiles(Path.Combine(root, "store", "covers"), "*.png").First();
                var coverId = Guid.NewGuid().ToString("N"); File.Copy(sourceCover, paths.CoverPath(coverId)!);
                var edited = model.VisibleGames[0];
                dialogs.EditResult = edited.Entry with { CoverId = coverId, Name = "长中文游戏名称 · 星辰、探索与世界的另一端" };
                rows.ScrollIntoView(rows.Items[0]); await Task.Delay(100);
                await model.EditCommand.ExecuteAsync(edited);
                rows.ScrollIntoView(model.Rows.First(row => row.Items.Contains(edited)));
                await WaitUntilAsync(() => edited.Image is not null); window.UpdateLayout(); Capture(window, "local-cover.png");
                Check(edited.Image is not null && edited.HasCover, "editing cover reloads visible artwork without recreating game record");
                model.ListView = true; await Task.Delay(220); window.UpdateLayout(); Capture(window, "list-1000.png");
                var list = (ListBox)window.FindName("GameList");
                var listRealized = Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null);
                Check(listRealized > 0 && listRealized < 40, $"list virtualized: {listRealized}/1000 realized");
                model.SearchText = "没有这个游戏"; model.Refresh(); await Task.Delay(100); Capture(window, "no-results.png");
                Check(model.IsNoResults, "search empty result state");
                model.SearchText = ""; model.Refresh(); model.ListView = false;
                window.Width = 860; window.Height = 560; await Task.Delay(100); window.UpdateLayout(); Capture(window, "compact-860.png");
                Check(window.ActualWidth <= 860 && rows.ActualWidth > 400, "minimum window maintains usable library width");
                _ = window.Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(220);
                    var detail = app.Windows.OfType<Window>().Single(w => w != window && w.Title == "游戏详情");
                    Capture(detail, "narrow-details.png"); model.CloseDetailsCommand.Execute(null);
                });
                model.SelectCommand.Execute(model.VisibleGames[0]);
                Check(model.SelectedGame is null, "narrow-window detail dialog opens and Escape command closes it");
                window.Width = 1180; window.Height = 760; await Task.Delay(100);
                model.SelectCommand.Execute(model.VisibleGames[0]); await Task.Delay(200); window.UpdateLayout(); Capture(window, "details.png");
                Check(((FrameworkElement)window.FindName("DetailsPane")).Visibility == Visibility.Visible, "wide-window detail drawer appears");
                model.SelectCommand.Execute(model.VisibleGames[1]);
                await Task.Delay(30); model.SelectCommand.Execute(model.VisibleGames[2]);
                model.CloseDetailsCommand.Execute(null); await Task.Delay(230);
                Check(model.SelectedGame is null && ((FrameworkElement)window.FindName("DetailsPane")).Visibility == Visibility.Collapsed && Motion.ActiveAnimationCount == 0,
                    "rapid detail selection and close cancel animations without reviving stale selection");
                model.CloseDetailsCommand.Execute(null);
                var editor = new GameEditorWindow(new GameEditorViewModel(Game("长中文名称 · 星辰与世界的另一端"), false, new CoverService(paths))) { Owner = window };
                editor.Show(); await Task.Delay(220); editor.UpdateLayout(); Capture(editor, "editor.png"); editor.Close();
                var settings = new DataSettingsWindow { Owner = window, DataContext = model };
                settings.Show(); await Task.Delay(220); settings.UpdateLayout(); Capture(settings, "settings.png"); settings.Close();
                Check(Motion.ActiveAnimationCount == 0, "all animation clocks released after dialogs settle");
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; app.Shutdown(1); }
        }, DispatcherPriority.ApplicationIdle);
        app.Run(window);
        if (failure is not null) throw failure;
        Check(GlassSurface.ActiveEffectCount == 0, "window shutdown releases all glass animation clocks");
        Check(LiquidBackdrop.ActiveBackgroundCount == 0, "window shutdown releases the shader background clock");
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    { for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException("UI initialization timed out"); }
    private static async Task MotionChecksAsync(Window window, Button primary)
    {
        // Force the test branch independently of this machine's Windows animation preference.
        Motion.SetEnabled(window, true);
        var header = (FrameworkElement)window.FindName("PageHeader");
        Motion.Reveal(header, y: 8, duration: 180);
        Check(Motion.ActiveAnimationCount > 0, "visible entrance creates native animation clocks");
        await Task.Delay(35); Motion.Reveal(header, y: 8, duration: 120);
        await Task.Delay(180);
        Check(header.Opacity == 1 && !header.HasAnimatedProperties && Motion.ActiveAnimationCount == 0,
            "interrupted entrance converges and removes completed clocks");
        Motion.Reveal(header, duration: 180);
        Motion.SetEnabled(window, false);
        Check(header.Opacity == 1 && !header.HasAnimatedProperties && Motion.ActiveAnimationCount == 0,
            "reduced motion cancels in-flight animations immediately");
        Motion.Reveal(header); Motion.Scale(header, 0.8);
        var transform = (TransformGroup)header.RenderTransform;
        Check(header.Opacity == 1 && transform.Children.OfType<ScaleTransform>().Single().ScaleX == 1 && Motion.ActiveAnimationCount == 0,
            "reduced motion keeps content visible and transforms neutral");
        Motion.SetEnabled(window, true);
        Motion.Reveal(header); header.Visibility = Visibility.Collapsed;
        Check(Motion.ActiveAnimationCount == 0 && !header.HasAnimatedProperties, "hidden visual cancels its animations");
        header.Visibility = Visibility.Visible;
        primary.ApplyTemplate();
        var frame = (FrameworkElement)primary.Template.FindName("Frame", primary);
        Motion.Scale(frame, 0.985, 85); await Task.Delay(20); Motion.Scale(frame, 1, 140); await Task.Delay(190);
        Check(((TransformGroup)frame.RenderTransform).Children.OfType<ScaleTransform>().Single().ScaleX == 1 && Motion.ActiveAnimationCount == 0,
            "button press interrupted by release returns to its original size");
        primary.IsEnabled = false; window.UpdateLayout();
        Check(frame.Opacity == 0.45, "button motion preserves the disabled template state");
        primary.ClearValue(UIElement.IsEnabledProperty);
        Motion.Reveal(header); window.WindowState = WindowState.Minimized;
        Check(Motion.ActiveAnimationCount == 0, "minimizing the window releases active animation clocks");
        window.WindowState = WindowState.Normal;
    }
    private static async Task GlassChecksAsync(Window window, ListBox rows)
    {
        window.Activate(); Motion.SetEnabled(window, true); await Task.Delay(80);
        var card = Descendants<GlassSurface>(rows).First(surface => surface.Interactive);
        var original = card.TranslatePoint(new Point(), rows);
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        await Task.Delay(120);
        Check(card.IsSweeping && GlassSurface.ActiveEffectCount <= 2, "hover reflection is limited to one card and the foreground header band");
        Check(card.TranslatePoint(new Point(), rows).Y < original.Y - 1 && card.LayoutTransform == Transform.Identity, "card lift changes rendering without reflowing the virtualized rows");
        Capture(window, "card-shimmer.png");
        Motion.SetEnabled(window, false);
        Check(!card.IsSweeping && GlassSurface.ActiveEffectCount == 0, "reduced motion cancels both looping light and in-flight card sheen");
        Motion.SetEnabled(window, true);
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        card.Visibility = Visibility.Collapsed;
        Check(!card.IsSweeping && GlassSurface.ActiveEffectCount <= 1, "hidden card releases its reflection clock");
        card.Visibility = Visibility.Visible;
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        await Task.Delay(780);
        Check(!card.IsSweeping && GlassSurface.ActiveEffectCount <= 1, "one-shot card sheen removes its completed clock");
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        window.WindowState = WindowState.Minimized;
        Check(GlassSurface.ActiveEffectCount == 0, "minimized window suspends every glass effect");
        window.WindowState = WindowState.Normal; window.Activate(); await Task.Delay(60);
        var overlay = new Window { Owner = window, Width = 300, Height = 200, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        overlay.Show(); overlay.Activate(); await Task.Delay(80);
        Check(GlassSurface.ActiveEffectCount == 0, "background window suspends ambient glass animation");
        overlay.Close(); window.Activate(); await Task.Delay(60);
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        rows.Visibility = Visibility.Collapsed;
        Check(GlassSurface.ActiveEffectCount <= 1, "recycled or hidden card collections retain no reflection clocks");
        rows.Visibility = Visibility.Visible; await Task.Delay(30);
    }
    private static void FluidFieldChecks()
    {
        var field = new LiquidFlowField();
        for (var i = 0; i < 60; i++) field.Step(1.0 / 60, 1.5);
        Check(field.Energy == 0 && field.SampleOffset(0.5, 0.5) == (0, 0), "idle flow is stable without pointer input");
        field.Stir(0.3, 0.5, 0.7, 0.5, 1.5);
        for (var i = 0; i < 12; i++) field.Step(1.0 / 60, 1.5);
        var first = field.SampleOffset(0.5, 0.5); var energy = field.Energy;
        for (var i = 0; i < 12; i++) field.Step(1.0 / 60, 1.5);
        Check(first.X < -0.001 && field.SampleOffset(0.5, 0.5) != first && field.Energy > 0 && field.Energy < energy,
            "a fast stroke transports color and continues moving after release with decaying momentum");
        for (var i = 0; i < 600; i++)
        {
            field.Stir(0.5 + Math.Sin(i * 0.3) * 0.35, 0.5 + Math.Cos(i * 0.3) * 0.35,
                0.5 + Math.Sin((i + 1) * 0.3) * 0.35, 0.5 + Math.Cos((i + 1) * 0.3) * 0.35, 1.5);
            field.Step(1.0 / 60, 1.5);
        }
        var pixels = new byte[LiquidFlowField.Width * 2 * LiquidFlowField.Height * 4]; field.CopyPixels(pixels);
        Check(double.IsFinite(field.Energy) && field.Energy < 2.25 && Enumerable.Range(0, pixels.Length / 4).All(i => pixels[i * 4 + 3] == 255),
            "sustained rapid stirring stays finite and uses opaque flow texture channels");
        for (var i = 0; i < 600; i++) field.Step(1.0 / 60, 1.5);
        Check(field.Energy < 0.000001 && Math.Abs(field.SampleOffset(0.5, 0.5).X) < 0.001,
            "velocity and displacement both settle without replacing trail slots");
    }
    private static async Task LiquidChecksAsync(Window window)
    {
        var backdrop = (LiquidBackdrop)window.FindName("WindowLiquid");
        window.Activate(); Motion.SetEnabled(window, true); await Task.Delay(100);
        using var shader = Application.GetResourceStream(new Uri("/CXLauncher;component/Assets/Shaders/liquid.ps", UriKind.Relative))!.Stream;
        var header = new byte[4]; shader.ReadExactly(header);
        Check(header.SequenceEqual(new byte[] { 0, 3, 255, 255 }) && shader.Length > 128, "compiled native ps_3_0 shader is embedded in the application");
        var first = backdrop.RenderFrame(0); var second = backdrop.RenderFrame(6);
        var a = new byte[first.PixelWidth * first.PixelHeight * 4]; var b = new byte[a.Length];
        first.CopyPixels(a, first.PixelWidth * 4, 0); second.CopyPixels(b, first.PixelWidth * 4, 0);
        Check(!a.SequenceEqual(b) && a.Where((_, index) => index % 4 != 3).Distinct().Count() > 100, "liquid field changes over time and contains a continuous spectrum");
        var saturation = Enumerable.Range(0, a.Length / 4).Select(index =>
        {
            var maximum = Math.Max(a[index * 4], Math.Max(a[index * 4 + 1], a[index * 4 + 2]));
            var minimum = Math.Min(a[index * 4], Math.Min(a[index * 4 + 1], a[index * 4 + 2]));
            return (maximum - minimum) / (double)Math.Max(1, (int)maximum);
        }).ToArray();
        Check(saturation.Average() < 0.22 && saturation.Max() < 0.32 && Enumerable.Range(0, a.Length / 4).All(i => a[i * 4 + 2] <= a[i * 4 + 1] || a[i * 4] <= a[i * 4 + 1]), "graphite palette stays low saturation and excludes purple across the full field");
        Check(backdrop.UsesGpu ? backdrop.IsRunning && LiquidBackdrop.ActiveBackgroundCount == 1 : !backdrop.IsRunning && backdrop.Source is BitmapSource,
            $"background selects hardware shader or static fallback: GPU={backdrop.UsesGpu}, tier={RenderCapability.Tier >> 16}");
        var before = backdrop.CurrentTime; await Task.Delay(150);
        Check(!backdrop.UsesGpu || backdrop.CurrentTime > before, "foreground shader time advances on capable hardware");
        if (backdrop.UsesGpu)
        {
            backdrop.Stir(new Point(0.35, 0.4)); await Task.Delay(100);
            var sampleTime = backdrop.CurrentTime;
            var calm = backdrop.RenderFrame(sampleTime); calm.CopyPixels(a, calm.PixelWidth * 4, 0);
            backdrop.Stir(new Point(0.52, 0.48)); await Task.Delay(50);
            var stirred = backdrop.RenderFrame(sampleTime); stirred.CopyPixels(b, stirred.PixelWidth * 4, 0);
            Check(!a.SequenceEqual(b) && backdrop.ActiveDisturbanceCount > 0, "pointer drag distorts the liquid field at the same shader time");
            for (var i = 0; i < 6; i++) { await Task.Delay(100); backdrop.Stir(new Point(0.45 + i * 0.03, 0.5 + Math.Sin(i) * 0.1)); }
            Check(backdrop.ActiveDisturbanceCount > 0, "pointer trail retains fluid momentum between frames");
            Capture(window, "liquid-stirred.png"); await Task.Delay(2700);
            Check(backdrop.ActiveDisturbanceCount == 0, "liquid disturbances dissipate after pointer movement stops");
        }
        Motion.SetEnabled(window, false); var paused = backdrop.CurrentTime; await Task.Delay(100);
        Check(!backdrop.IsRunning && LiquidBackdrop.ActiveBackgroundCount == 0 && backdrop.CurrentTime == paused, "reduced motion freezes the current shader frame without a ticking clock");
        var still = backdrop.RenderFrame(paused); still.CopyPixels(a, still.PixelWidth * 4, 0);
        backdrop.Stir(new Point(0.2, 0.2)); backdrop.Stir(new Point(0.8, 0.8));
        still = backdrop.RenderFrame(paused); still.CopyPixels(b, still.PixelWidth * 4, 0);
        Check(a.SequenceEqual(b), "reduced motion also disables pointer stirring");
        Motion.SetEnabled(window, true); await Task.Delay(80);
        Check(!backdrop.UsesGpu || backdrop.IsRunning && backdrop.CurrentTime > paused, "shader resumes from the paused frame when animations are restored");
        backdrop.Visibility = Visibility.Collapsed;
        Check(LiquidBackdrop.ActiveBackgroundCount == 0, "hidden shader releases its rendering clock");
        backdrop.Visibility = Visibility.Visible; window.WindowState = WindowState.Minimized;
        Check(LiquidBackdrop.ActiveBackgroundCount == 0, "minimized window suspends the entire dynamic background");
        window.WindowState = WindowState.Normal; window.Activate(); await Task.Delay(50);
        var overlay = new Window { Owner = window, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 220, Height = 160 };
        overlay.Show(); overlay.Activate(); await Task.Delay(50);
        Check(LiquidBackdrop.ActiveBackgroundCount == 0, "background shader pauses when another window takes focus");
        overlay.Close(); window.Activate(); await Task.Delay(80); Capture(window, "liquid-theme.png");
        var previousEffect = backdrop.Effect;
        using (backdrop.PrepareSnapshot()) Check(backdrop.Effect is null && backdrop.Source is BitmapSource { IsFrozen: true }, "software bitmap capture has a visible frozen field instead of an unsupported ps_3_0 effect");
        Check(backdrop.Effect == previousEffect, "bitmap capture restores the live shader renderer");
    }
    private static void Capture(Window window, string name)
    {
        var visual = (FrameworkElement)window.Content;
        var snapshots = Descendants<LiquidBackdrop>(visual).Select(backdrop => backdrop.PrepareSnapshot()).ToArray();
        try
        {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var width = visual.ActualWidth + visual.Margin.Left + visual.Margin.Right;
        var height = visual.ActualHeight + visual.Margin.Top + visual.Margin.Bottom;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen()) context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(root, name)); encoder.Save(stream);
        }
        finally { foreach (var snapshot in snapshots) snapshot.Dispose(); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed record ProbeResult(string[] Arguments, string WorkingDirectory, int ProcessId);
    private sealed class TestDialogs : IAppDialogs
    {
        public int ConfirmationCount { get; private set; }
        public string? ImportPath { get; set; }
        public GameEntry? EditResult { get; set; }
        public string[] ChooseGames() => [];
        public string? ChooseImport() => ImportPath;
        public string? ChooseExport() => null;
        public bool Confirm(string title, string message) { ConfirmationCount++; return true; }
        public Task<GameEntry?> EditAsync(GameEntry entry, bool isNew) => Task.FromResult(EditResult);
        public void ShowDataSettings(MainViewModel model) { }
    }
}
