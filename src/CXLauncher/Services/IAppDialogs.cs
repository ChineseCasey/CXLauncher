using CXLauncher.Models;
using CXLauncher.ViewModels;

namespace CXLauncher.Services;

public interface IAppDialogs
{
    string[] ChooseGames();
    string? ChooseImport();
    string? ChooseExport();
    bool Confirm(string title, string message);
    Task<GameEntry?> EditAsync(GameEntry entry, bool isNew);
    void ShowDataSettings(MainViewModel viewModel);
}
