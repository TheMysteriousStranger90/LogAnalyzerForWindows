using LogAnalyzerForWindows.ViewModels;

namespace LogAnalyzerForWindows.Interfaces;

internal interface IDialogService
{
    Task ShowSettingsDialogAsync(SettingsViewModel viewModel);
    Task<bool> ShowConfirmationDialogAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
}
