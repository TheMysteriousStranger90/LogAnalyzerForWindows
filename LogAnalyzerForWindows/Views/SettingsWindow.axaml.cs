using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LogAnalyzerForWindows.ViewModels;

namespace LogAnalyzerForWindows.Views;

internal sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.SettingsSaved += OnSettingsSaved;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSettingsSaved()
    {
        // Optionally close after save
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SettingsSaved -= OnSettingsSaved;
        }
        base.OnClosed(e);
    }
}
