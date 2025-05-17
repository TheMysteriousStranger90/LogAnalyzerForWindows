using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Filter;
using LogAnalyzerForWindows.Formatter;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Helpers;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Mail;
using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Analyzer;
using LogAnalyzerForWindows.Models.Reader;
using LogAnalyzerForWindows.Models.Reader.Interfaces;
using LogAnalyzerForWindows.Models.Writer;
using LogAnalyzerForWindows.Models.Writer.Interfaces;
using LogAnalyzerForWindows.Services;

namespace LogAnalyzerForWindows.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged, IDisposable
{
    private readonly IEmailService _emailService;
    private readonly IFileSystemService _fileSystemService;
    private readonly LogMonitor _monitor;
    private readonly FileSystemWatcher _folderWatcher;

    private string _selectedLogLevel;
    private string _selectedTime;
    private ICommand _startCommand;
    private ICommand _stopCommand;
    private ICommand _sendEmailCommand;
    private Action<IEnumerable<LogEntry>> _onLogsChangedHandler;

    private readonly HashSet<LogEntry> _processedLogs = new HashSet<LogEntry>();

    public AvaloniaList<string> LogLevels { get; } = new AvaloniaList<string>
        { "Information", "Warning", "Error", "AuditSuccess", "AuditFailure" };

    public AvaloniaList<string> Times { get; } =
        new AvaloniaList<string> { "Last hour", "Last 24 hours", "Last 3 days" };

    public AvaloniaList<string> Formats { get; } = new AvaloniaList<string> { "txt", "json" };

    private string _textBlock;

    public string TextBlock
    {
        get => _textBlock;
        set => SetProperty(ref _textBlock, value);
    }

    private string _outputText = string.Empty;

    public string OutputText
    {
        get => _outputText;
        set { SetProperty(ref _outputText, value); }
    }

    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (SetProperty(ref _selectedLogLevel, value))
            {
                (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedTime
    {
        get => _selectedTime;
        set
        {
            if (SetProperty(ref _selectedTime, value))
            {
                (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private string _selectedFormat = "txt";

    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                UpdateCanSaveState();
            }
        }
    }


    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _canSave;

    public bool CanSave
    {
        get => _canSave;
        private set => SetProperty(ref _canSave, value);
    }

    private string _userEmail;

    public string UserEmail
    {
        get => _userEmail;
        set
        {
            if (SetProperty(ref _userEmail, value))
            {
                (_sendEmailCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private static string DefaultLogFolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LogAnalyzerForWindows");

    public bool IsFolderExists => Directory.Exists(DefaultLogFolderPath);


    public ICommand StartCommand
    {
        get => _startCommand;
        set => SetProperty(ref _startCommand, value);
    }

    public ICommand StopCommand
    {
        get => _stopCommand;
        set => SetProperty(ref _stopCommand, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ArchiveLatestFolderCommand { get; }


    public ICommand SendEmailCommand => _sendEmailCommand ??= new RelayCommand(
        async () => await SendEmailAsync(),
        CanSendEmail
    );

    private bool CanSendEmail()
    {
        if (!EmailSender.IsValidEmail(UserEmail)) return false;
        try
        {
            return Directory.Exists(DefaultLogFolderPath) && Directory.GetFiles(DefaultLogFolderPath, "*.zip").Any();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking for zip files: {ex.Message}");
            return false;
        }
    }


    public MainWindowViewModel()
    {
        _emailService = new EmailService();
        _fileSystemService = new FileSystemService();

        _monitor = new LogMonitor();
        _monitor.MonitoringStarted += OnMonitoringStateChanged;
        _monitor.MonitoringStopped += OnMonitoringStateChanged;

        StartCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
        StopCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);
        SaveCommand = new RelayCommand(SaveLogs, () => CanSave);
        OpenFolderCommand = new RelayCommand(OpenLogFolder, () => IsFolderExists);
        ArchiveLatestFolderCommand = new RelayCommand(ArchiveLogFolder, () => IsFolderExists);

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(Path.Combine(documentsPath, "LogAnalyzerForWindows")))
        {
            Directory.CreateDirectory(Path.Combine(documentsPath, "LogAnalyzerForWindows"));
        }

        _folderWatcher = new FileSystemWatcher(documentsPath)
        {
            Filter = "LogAnalyzerForWindows",
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
            IncludeSubdirectories = true
        };
        _folderWatcher.Created += OnLogDirectoryChanged;
        _folderWatcher.Deleted += OnLogDirectoryChanged;
        _folderWatcher.Renamed += OnLogDirectoryChanged;
        _folderWatcher.Changed += OnLogDirectoryChanged;

        try
        {
            _folderWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enable FileSystemWatcher: {ex.Message}");
        }

        OnPropertyChanged(nameof(IsFolderExists));
        UpdateCanSaveState();
    }

    private void UpdateCanSaveState()
    {
        CanSave = _processedLogs.Any() && !string.IsNullOrEmpty(SelectedFormat);
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool CanStartMonitoring() => !string.IsNullOrEmpty(SelectedLogLevel) &&
                                         !string.IsNullOrEmpty(SelectedTime) && !_monitor.IsMonitoring;

    private bool CanStopMonitoring() => _monitor.IsMonitoring;

    private void StartMonitoring()
    {
        if (!CanStartMonitoring()) return;

        IsLoading = true;
        TextBlock = "Starting monitoring...";
        OutputText = string.Empty;
        _processedLogs.Clear();
        UpdateCanSaveState();

        Task.Run(() =>
        {
            ILogReader reader = new WindowsEventLogReader("System");
            LogAnalyzer generalAnalyzer = new LevelLogAnalyzer(SelectedLogLevel);
            ILogFormatter formatter = new LogFormatter();
            ILogWriter writer = new TextBoxLogWriter(formatter, UpdateOutputTextOnUiThread);

            LogManager manager = new LogManager(reader, generalAnalyzer, formatter, writer);

            TimeSpan timeSpan;
            switch (SelectedTime)
            {
                case "Last hour":
                    timeSpan = TimeSpan.FromHours(1);
                    break;
                case "Last 24 hours":
                    timeSpan = TimeSpan.FromDays(1);
                    break;
                case "Last 3 days":
                    timeSpan = TimeSpan.FromDays(3);
                    break;
                default:
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        TextBlock = $"Error: Unknown time interval '{SelectedTime}'.";
                        IsLoading = false;
                        OnMonitoringStateChanged();
                    });
                    return;
            }

            TimeFilter timeFilter = new TimeFilter(timeSpan);

            _onLogsChangedHandler = (incomingLogs) =>
            {
                var relevantLogs = timeFilter.Filter(incomingLogs);

                var levelAnalyzer = new LevelLogAnalyzer(SelectedLogLevel);

                var newUniqueLevelLogs = levelAnalyzer.FilterByLevel(relevantLogs)
                    .Where(log => _processedLogs.Add(log))
                    .ToList();

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateCanSaveState();

                    if (newUniqueLevelLogs.Any())
                    {
                        manager.ProcessLogs(newUniqueLevelLogs);
                        TextBlock =
                            $"Monitoring... Unique '{SelectedLogLevel}' logs found: {_processedLogs.Count(l => l.Level?.Equals(SelectedLogLevel, StringComparison.OrdinalIgnoreCase) ?? false)}";
                    }
                });
            };

            _monitor.LogsChanged += _onLogsChangedHandler;
            _monitor.Monitor(reader);
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Debug.WriteLine($"Monitoring task faulted: {task.Exception?.GetBaseException().Message}");
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = $"Error during monitoring: {task.Exception?.GetBaseException().Message}";
                    IsLoading = false;
                    if (_monitor.IsMonitoring) _monitor.StopMonitoring();
                    else OnMonitoringStateChanged();
                });
            }
        });
    }

    private void StopMonitoring()
    {
        TextBlock = "Stopping monitoring...";

        try
        {
            if (_monitor?.IsMonitoring == true)
            {
                _monitor.StopMonitoring();
            }

            if (_onLogsChangedHandler != null && _monitor != null)
            {
                _monitor.LogsChanged -= _onLogsChangedHandler;
                _onLogsChangedHandler = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping monitoring: {ex.Message}");
        }
    }

    private void SaveLogs()
    {
        if (string.IsNullOrEmpty(SelectedFormat) || !_processedLogs.Any())
        {
            TextBlock = "No logs to save or format not selected.";
            return;
        }

        TextBlock = "Saving logs...";
        IsLoading = true;

        Task.Run(() =>
        {
            try
            {
                ILogFormatter formatter = SelectedFormat.ToLowerInvariant() switch
                {
                    "json" => new JsonLogFormatter(),
                    "txt" => new LogFormatter(),
                    _ => throw new InvalidOperationException($"Unknown format: {SelectedFormat}")
                };

                var linesToSave = _processedLogs.Select(log =>
                {
                    if (formatter is JsonLogFormatter)
                    {
                        return formatter.Format(log).Message;
                    }

                    return formatter.Format(log).ToString();
                });

                string logsContent = string.Join(Environment.NewLine, linesToSave);
                string filePath = LogPathHelper.GetLogFilePath(SelectedFormat);
                File.WriteAllText(filePath, logsContent);

                Dispatcher.UIThread.InvokeAsync(() => TextBlock = $"Logs saved to: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving logs: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(() => TextBlock = $"Error saving logs: {ex.Message}");
            }
            finally
            {
                Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        });
    }

    private void OpenLogFolder()
    {
        _fileSystemService.OpenFolder(DefaultLogFolderPath, UpdateTextBlockOnUiThread);
    }

    private void ArchiveLogFolder()
    {
        TextBlock = "Archiving...";
        IsLoading = true;
        Task.Run(() =>
        {
            _fileSystemService.ArchiveLatestFolder(DefaultLogFolderPath, UpdateTextBlockOnUiThread);
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = false;
                (_sendEmailCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        });
    }

    private async Task SendEmailAsync()
    {
        TextBlock = "Sending email...";
        IsLoading = true;
        try
        {
            var zipFiles = Directory.GetFiles(DefaultLogFolderPath, "*.zip");
            if (!zipFiles.Any())
            {
                TextBlock = "No archive files found to send.";
                IsLoading = false;
                return;
            }

            var latestZipFile = zipFiles.OrderByDescending(File.GetCreationTimeUtc).First();

            await _emailService.SendEmailAsync("Log Analysis Recipient", UserEmail, "Log Analyzer For Windows - Logs",
                $"Please find the latest log archive attached ({Path.GetFileName(latestZipFile)}).", latestZipFile);
            TextBlock = "Email sent successfully.";
        }
        catch (InvalidOperationException ioEx)
        {
            Debug.WriteLine($"Operation error sending email: {ioEx.Message}");
            if (ioEx.Message.Contains("Email service is not configured"))
            {
                TextBlock = "Error sending email: Email service is not configured. Please check settings.";
            }
            else
            {
                TextBlock = $"Error sending email: An operation error occurred ({ioEx.Message})";
            }
        }
        catch (SmtpException smtpEx)
        {
            Debug.WriteLine($"SMTP error sending email: {smtpEx.StatusCode} - {smtpEx.Message}");
            string userMessage = $"SMTP Error: {smtpEx.Message}";

            if (smtpEx.InnerException is SocketException)
            {
                userMessage =
                    "Error sending email: Network connection issue or email server unavailable. Please check your internet connection and server status.";
            }
            else
            {
                switch (smtpEx.StatusCode)
                {
                    case SmtpStatusCode.MailboxUnavailable:
                        userMessage = "Error sending email: Recipient mailbox unavailable or does not exist.";
                        break;
                    case SmtpStatusCode.ServiceNotAvailable:
                        userMessage =
                            "Error sending email: Email service is temporarily unavailable. Please try again later.";
                        break;
                    case SmtpStatusCode.ClientNotPermitted:
                    case SmtpStatusCode.TransactionFailed:
                        userMessage =
                            "Error sending email: Authentication failed or transaction rejected by the email server. Please check your email credentials and server policy.";
                        break;
                    case SmtpStatusCode.MustIssueStartTlsFirst:
                        userMessage =
                            "Error sending email: Secure connection (TLS) required by the server was not established.";
                        break;
                    default:
                        // The default userMessage (smtpEx.Message) will be used if no specific case matches.
                        break;
                }
            }

            TextBlock = userMessage;
        }
        catch (IOException fileEx)
        {
            Debug.WriteLine($"File error during email preparation: {fileEx.Message}");
            TextBlock = $"Error preparing email attachment: {fileEx.Message}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Generic error sending email: {ex.Message}");
            TextBlock = $"An unexpected error occurred while sending email: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnLogDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(IsFolderExists));
            (OpenFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ArchiveLatestFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (_sendEmailCommand as RelayCommand)?.RaiseCanExecuteChanged();
        });
    }

    private void OnMonitoringStateChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();

            if (_monitor.IsMonitoring)
            {
                TextBlock = "Monitoring started.";
                IsLoading = false;
            }
            else
            {
                TextBlock = "Monitoring stopped.";
                IsLoading = false;
            }
        });
    }


    private void UpdateTextBlockOnUiThread(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => TextBlock = message);
    }

    private void UpdateOutputTextOnUiThread(string text)
    {
        Dispatcher.UIThread.InvokeAsync(() => { OutputText += text + Environment.NewLine; });
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                try
                {
                    if (_monitor?.IsMonitoring == true)
                    {
                        StopMonitoring();
                    }

                    _monitor.MonitoringStarted -= OnMonitoringStateChanged;
                    _monitor.MonitoringStopped -= OnMonitoringStateChanged;

                    if (_folderWatcher != null)
                    {
                        _folderWatcher.Created -= OnLogDirectoryChanged;
                        _folderWatcher.Deleted -= OnLogDirectoryChanged;
                        _folderWatcher.Renamed -= OnLogDirectoryChanged;
                        _folderWatcher.Changed -= OnLogDirectoryChanged;
                        _folderWatcher.EnableRaisingEvents = false;
                        _folderWatcher.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during Dispose: {ex.Message}");
                }
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