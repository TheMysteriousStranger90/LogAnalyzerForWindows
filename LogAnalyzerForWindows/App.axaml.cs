using Avalonia;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

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

        services.AddSingleton<ILogRepository, LogRepository>();

        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<ILogMonitor, LogMonitor>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<Func<ILogRepository, PaginationViewModel>>(sp =>
            repository => new PaginationViewModel(repository));
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
