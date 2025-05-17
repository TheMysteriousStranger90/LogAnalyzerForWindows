using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogAnalyzerForWindows.ViewModels;
using System;

namespace LogAnalyzerForWindows.Views;

public partial class MainWindow : Window, IDisposable
{
    private MainWindowViewModel _viewModel;
    private bool _disposedValue;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Dispose();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _viewModel?.Dispose();
                Closing -= MainWindow_Closing;
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}