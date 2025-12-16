using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LogAnalyzerForWindows.Database;
using LogAnalyzerForWindows.Database.Repositories;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Services;
using LogAnalyzerForWindows.ViewModels;
using LogAnalyzerForWindows.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows;

internal sealed class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            var trayService = _serviceProvider.GetRequiredService<ITrayIconService>();

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            trayService.Initialize(mainWindow);
            trayService.ExitRequested += () => desktop.Shutdown();

            var settings = settingsService.GetGeneralSettings();
            if (settings.MinimizeToTray)
            {
                mainWindow.Closing += (sender, e) =>
                {
                    if (sender is Window window && !_isShuttingDown)
                    {
                        e.Cancel = true;
                        trayService.MinimizeToTray();
                    }
                };
            }

            desktop.MainWindow = mainWindow;

            if (settings.StartMinimized && settings.MinimizeToTray)
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.ShowInTaskbar = false;
                trayService.MinimizeToTray();
            }

            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private bool _isShuttingDown;

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextFactory<LogAnalyzerDbContext>(options =>
        {
            options.UseSqlite(DbContextConfig.ConnectionString);
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<ILogRepository, LogRepository>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<ILogMonitor, LogMonitor>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<Func<ILogRepository, PaginationViewModel>>(sp =>
            repository => new PaginationViewModel(repository));
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _isShuttingDown = true;

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
