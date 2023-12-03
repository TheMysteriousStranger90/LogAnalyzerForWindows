using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogAnalyzerForWindows.ViewModels;

namespace LogAnalyzerForWindows.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel viewModel => (MainWindowViewModel)DataContext;
    public MainWindow()
    {
        Debug.WriteLine("Initializing main window.");
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}