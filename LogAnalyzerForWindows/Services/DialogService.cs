using Avalonia.Controls.ApplicationLifetimes;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.ViewModels;
using LogAnalyzerForWindows.Views;

namespace LogAnalyzerForWindows.Services;

internal sealed class DialogService : IDialogService
{
    public async Task ShowSettingsDialogAsync(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (Avalonia.Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is not null)
        {
            var settingsWindow = new SettingsWindow(viewModel);
            await settingsWindow.ShowDialog(desktop.MainWindow).ConfigureAwait(true);
        }
    }

    public async Task<bool> ShowConfirmationDialogAsync(string title, string message)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
