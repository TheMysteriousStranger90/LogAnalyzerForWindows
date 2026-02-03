using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.ViewModels;

internal sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IEmailService _emailService;

    private string _smtpServer = string.Empty;
    private int _smtpPort = 587;
    private string _fromEmail = string.Empty;
    private string _fromName = string.Empty;
    private string _password = string.Empty;
    private bool _useTls = true;

    private bool _autoStartWithWindows;
    private bool _minimizeToTray;
    private bool _startMinimized;

    private string _statusMessage = string.Empty;
    private bool _isSaving;
    private bool _isTesting;
    private string _testEmail = string.Empty;

    public string SmtpServer
    {
        get => _smtpServer;
        set
        {
            if (SetProperty(ref _smtpServer, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public int SmtpPort
    {
        get => _smtpPort;
        set
        {
            if (SetProperty(ref _smtpPort, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public string FromEmail
    {
        get => _fromEmail;
        set
        {
            if (SetProperty(ref _fromEmail, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public string FromName
    {
        get => _fromName;
        set => SetProperty(ref _fromName, value);
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool UseTls
    {
        get => _useTls;
        set => SetProperty(ref _useTls, value);
    }

    public bool AutoStartWithWindows
    {
        get => _autoStartWithWindows;
        set => SetProperty(ref _autoStartWithWindows, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set
        {
            if (SetProperty(ref _isSaving, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (SetProperty(ref _isTesting, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public string TestEmail
    {
        get => _testEmail;
        set
        {
            if (SetProperty(ref _testEmail, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand TestSmtpCommand { get; }
    public ICommand ResetToDefaultCommand { get; }

    public event Action? SettingsSaved;

    public SettingsViewModel(ISettingsService settingsService, IEmailService emailService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));

        SaveCommand = new AsyncRelayCommand(
            SaveSettingsAsync,
            () => !IsSaving && !IsTesting);

        TestSmtpCommand = new AsyncRelayCommand(
            TestSmtpConnectionAsync,
            CanTestSmtp);

        ResetToDefaultCommand = new RelayCommand(ResetToDefault);

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.GetSettings();

        SmtpServer = settings.Smtp.Server;
        SmtpPort = settings.Smtp.Port;
        FromEmail = settings.Smtp.FromEmail;
        FromName = settings.Smtp.FromName;
        Password = settings.Smtp.Password;
        UseTls = settings.Smtp.UseTls;

        AutoStartWithWindows = settings.General.AutoStartWithWindows;
        MinimizeToTray = settings.General.MinimizeToTray;
        StartMinimized = settings.General.StartMinimized;

        TestEmail = FromEmail;
    }

    private async Task SaveSettingsAsync()
    {
        IsSaving = true;
        StatusMessage = "Saving settings...";

        try
        {
            var settings = new AppSettings
            {
                Smtp = new SmtpSettings
                {
                    Server = SmtpServer,
                    Port = SmtpPort,
                    FromEmail = FromEmail,
                    FromName = FromName,
                    Password = Password,
                    UseTls = UseTls
                },
                General = new GeneralSettings
                {
                    AutoStartWithWindows = AutoStartWithWindows,
                    MinimizeToTray = MinimizeToTray,
                    StartMinimized = StartMinimized
                }
            };

            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "Settings saved successfully!";
                SettingsSaved?.Invoke();
            });
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Error saving settings: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusMessage = $"Error saving settings: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsSaving = false);
        }
    }

    private bool CanTestSmtp()
    {
        return !IsTesting && !IsSaving &&
               !string.IsNullOrWhiteSpace(SmtpServer) &&
               SmtpPort > 0 &&
               !string.IsNullOrWhiteSpace(FromEmail) &&
               !string.IsNullOrWhiteSpace(Password) &&
               !string.IsNullOrWhiteSpace(TestEmail);
    }

    private async Task TestSmtpConnectionAsync()
    {
        IsTesting = true;
        StatusMessage = "Testing SMTP connection...";

        try
        {
            await SaveSettingsAsync().ConfigureAwait(false);

            await _emailService.SendEmailAsync(
                "Test Recipient",
                TestEmail,
                "AzioEventLog Analyzer - SMTP Test",
                "This is a test email from AzioEventLog Analyzer for Windows. If you received this, your SMTP settings are configured correctly.",
                string.Empty).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusMessage = $"Test email sent successfully to {TestEmail}!");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"SMTP test failed: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusMessage = $"SMTP test failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"SMTP test IO error: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusMessage = $"Connection error: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsTesting = false);
        }
    }

    private void ResetToDefault()
    {
        SmtpServer = string.Empty;
        SmtpPort = 587;
        FromEmail = string.Empty;
        FromName = string.Empty;
        Password = string.Empty;
        UseTls = true;

        AutoStartWithWindows = false;
        MinimizeToTray = false;
        StartMinimized = false;

        StatusMessage = "Settings reset to defaults. Click Save to apply.";
    }

    private void UpdateCommandStates()
    {
        (TestSmtpCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
        (SaveCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
    }
}
