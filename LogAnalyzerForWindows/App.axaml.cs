using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LogAnalyzerForWindows.Database;
using LogAnalyzerForWindows.Database.Repositories;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Services;
using LogAnalyzerForWindows.ViewModels;
using LogAnalyzerForWindows.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalyzerForWindows;

internal sealed class App : Application
{
    private IServiceProvider? _serviceProvider;
    private bool _isShuttingDown;

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
            var settings = settingsService.GetGeneralSettings();

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            mainWindow.Width = 1200;
            mainWindow.Height = 800;
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            trayService.Initialize(mainWindow);
            trayService.ExitRequested += () =>
            {
                _isShuttingDown = true;
                desktop.Shutdown();
            };

            if (settings.MinimizeToTray)
            {
                mainWindow.Closing += (sender, e) =>
                {
                    if (sender is Window && !_isShuttingDown)
                    {
                        e.Cancel = true;
                        trayService.MinimizeToTray();
                    }
                };
            }

            desktop.MainWindow = mainWindow;

            Program.SingleInstance?.StartListeningForActivation(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { trayService.ShowWindow(); });
            });

            if (settings.StartMinimized && settings.MinimizeToTray)
            {
                mainWindow.Opened += OnMainWindowOpenedForMinimize;

                void OnMainWindowOpenedForMinimize(object? s, EventArgs args)
                {
                    mainWindow.Opened -= OnMainWindowOpenedForMinimize;

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { trayService.MinimizeToTray(); },
                        Avalonia.Threading.DispatcherPriority.Background);
                }
            }

            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

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
        services.AddTransient<DashboardViewModel>();
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
