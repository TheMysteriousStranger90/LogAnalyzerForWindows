using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LogAnalyzerForWindows.Mail;
using LogAnalyzerForWindows.Services;
using LogAnalyzerForWindows.ViewModels;
using LogAnalyzerForWindows.Views;
using Microsoft.Extensions.Configuration;

namespace LogAnalyzerForWindows;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        IConfigurationRoot configuration = builder.Build();
        
        var smtpServer = configuration["EmailSettings:SmtpServer"];
        var smtpPort = Convert.ToInt32(configuration["EmailSettings:SmtpPort"]);
        var fromEmail = configuration["EmailSettings:FromEmail"];
        var fromName = configuration["EmailSettings:FromName"];
        var password = configuration["EmailSettings:Password"];
    
        var emailSender = new EmailSender(smtpServer, smtpPort, fromEmail, fromName, password);
        EmailService.EmailSender = emailSender;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}