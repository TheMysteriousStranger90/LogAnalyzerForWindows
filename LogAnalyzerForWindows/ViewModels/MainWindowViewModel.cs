using System.ComponentModel;
using System.Diagnostics;
using System.Net.Mail;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Filter;
using LogAnalyzerForWindows.Formatter;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Helpers;
using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Analyzer;
using LogAnalyzerForWindows.Models.Reader;
using LogAnalyzerForWindows.Models.Reader.Interfaces;
using LogAnalyzerForWindows.Models.Writer;
using LogAnalyzerForWindows.Models.Writer.Interfaces;
using LogAnalyzerForWindows.Services;

namespace LogAnalyzerForWindows.ViewModels;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly EmailService _emailService;
    private readonly FileSystemService _fileSystemService;
    private readonly LogMonitor _monitor;
    private readonly FileSystemWatcher _folderWatcher;

    private string _selectedLogLevel = string.Empty;
    private string _selectedTime = string.Empty;
    private ICommand? _startCommand;
    private ICommand? _stopCommand;
    private ICommand? _sendEmailCommand;
    private EventHandler<LogsChangedEventArgs>? _onLogsChangedHandler;

    private readonly HashSet<LogEntry> _processedLogs = [];

    public AvaloniaList<string> LogLevels { get; } =
        ["Information", "Warning", "Error", "AuditSuccess", "AuditFailure"];

    public AvaloniaList<string> Times { get; } = ["Last hour", "Last 24 hours", "Last 3 days"];

    public AvaloniaList<string> Formats { get; } = ["txt", "json"];

    private string _textBlock = string.Empty;

    public string TextBlock
    {
        get => _textBlock;
        set => SetProperty(ref _textBlock, value);
    }

    private string _outputText = string.Empty;

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (SetProperty(ref _selectedLogLevel, value))
            {
                (StartCommand as RelayCommand)?.OnCanExecuteChanged();
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
                (StartCommand as RelayCommand)?.OnCanExecuteChanged();
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

    private string _userEmail = string.Empty;

    public string UserEmail
    {
        get => _userEmail;
        set
        {
            if (SetProperty(ref _userEmail, value))
            {
                (_sendEmailCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    private static string DefaultLogFolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LogAnalyzerForWindows");

    public static bool IsFolderExists => Directory.Exists(DefaultLogFolderPath);

    public ICommand? StartCommand
    {
        get => _startCommand;
        set => SetProperty(ref _startCommand, value);
    }

    public ICommand? StopCommand
    {
        get => _stopCommand;
        set => SetProperty(ref _stopCommand, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ArchiveLatestFolderCommand { get; }

    public ICommand SendEmailCommand => _sendEmailCommand ??= new RelayCommand(
        async () => await SendEmailAsync().ConfigureAwait(false),
        CanSendEmail
    );

    private bool CanSendEmail()
    {
        if (!IsValidEmail(UserEmail)) return false;
        try
        {
            return Directory.Exists(DefaultLogFolderPath) &&
                   Directory.GetFiles(DefaultLogFolderPath, "*.zip").Length != 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Access denied checking for zip files: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error checking for zip files: {ex.Message}");
            return false;
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email;
        }
        catch (FormatException)
        {
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

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var logPath = Path.Combine(documentsPath, "LogAnalyzerForWindows");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
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
        catch (PlatformNotSupportedException ex)
        {
            Debug.WriteLine($"FileSystemWatcher not supported on this platform: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to enable FileSystemWatcher: {ex.Message}");
        }

        OnPropertyChanged(nameof(IsFolderExists));
        UpdateCanSaveState();
    }

    private void UpdateCanSaveState()
    {
        CanSave = _processedLogs.Count != 0 && !string.IsNullOrEmpty(SelectedFormat);
        (SaveCommand as RelayCommand)?.OnCanExecuteChanged();
    }

    private bool CanStartMonitoring() =>
        !string.IsNullOrEmpty(SelectedLogLevel) &&
        !string.IsNullOrEmpty(SelectedTime) &&
        !_monitor.IsMonitoring;

    private bool CanStopMonitoring() => _monitor.IsMonitoring;

    private void StartMonitoring()
    {
        if (!CanStartMonitoring()) return;

        IsLoading = true;
        TextBlock = "Starting monitoring...";
        OutputText = string.Empty;
        _processedLogs.Clear();
        UpdateCanSaveState();

        ILogReader reader = new WindowsEventLogReader("System");
        var generalAnalyzer = new LevelLogAnalyzer(SelectedLogLevel);
        ILogFormatter formatter = new LogFormatter();
        ILogWriter writer = new TextBoxLogWriter(formatter, UpdateOutputTextOnUiThread);

        var manager = new LogManager(reader, generalAnalyzer, formatter, writer);

        var timeSpan = SelectedTime switch
        {
            "Last hour" => TimeSpan.FromHours(1),
            "Last 24 hours" => TimeSpan.FromDays(1),
            "Last 3 days" => TimeSpan.FromDays(3),
            _ => TimeSpan.Zero
        };

        if (timeSpan == TimeSpan.Zero)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                TextBlock = $"Error: Unknown time interval '{SelectedTime}'.";
                IsLoading = false;
                OnMonitoringStateChanged(this, EventArgs.Empty);
            });
            return;
        }

        var timeFilter = new TimeFilter(timeSpan);

        // Виправлено: правильний EventHandler
        _onLogsChangedHandler = (sender, args) =>
        {
            var incomingLogs = args.Logs;
            var relevantLogs = timeFilter.Filter(incomingLogs);
            var levelAnalyzer = new LevelLogAnalyzer(SelectedLogLevel);

            var newUniqueLevelLogs = levelAnalyzer.FilterByLevel(relevantLogs)
                .Where(_processedLogs.Add)
                .ToList();

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateCanSaveState();

                if (newUniqueLevelLogs.Count != 0)
                {
                    manager.ProcessLogs(newUniqueLevelLogs);
                    var matchingCount = _processedLogs.Count(l =>
                        string.Equals(l.Level, SelectedLogLevel, StringComparison.OrdinalIgnoreCase));
                    TextBlock = $"Monitoring... Unique '{SelectedLogLevel}' logs found: {matchingCount}";
                }
            });
        };

        _monitor.LogsChanged += _onLogsChangedHandler;
        _monitor.Monitor(reader);
    }

    private void StopMonitoring()
    {
        TextBlock = "Stopping monitoring...";

        try
        {
            if (_monitor.IsMonitoring)
            {
                _monitor.StopMonitoring();
            }

            if (_onLogsChangedHandler is not null)
            {
                _monitor.LogsChanged -= _onLogsChangedHandler;
                _onLogsChangedHandler = null;
            }
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Invalid operation while stopping monitoring: {ex.Message}");
            throw;
        }
    }

    private void SaveLogs()
    {
        if (string.IsNullOrEmpty(SelectedFormat) || _processedLogs.Count == 0)
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
                ILogFormatter formatter = SelectedFormat.ToUpperInvariant() switch
                {
                    "JSON" => new JsonLogFormatter(),
                    "TXT" => new LogFormatter(),
                    _ => throw new InvalidOperationException($"Unknown format: {SelectedFormat}")
                };

                var linesToSave = _processedLogs.Select(log =>
                {
                    var formattedResult = formatter.Format(log);
                    return formattedResult.ToString() ?? string.Empty;
                });

                var logsContent = string.Join(Environment.NewLine, linesToSave);
                var filePath = LogPathHelper.GetLogFilePath(SelectedFormat);
                File.WriteAllText(filePath, logsContent);

                Dispatcher.UIThread.InvokeAsync(() => TextBlock = $"Logs saved to: {filePath}");
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO error saving logs: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(() => TextBlock = $"Error saving logs: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Access denied saving logs: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(() => TextBlock = $"Access denied: {ex.Message}");
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
                (_sendEmailCommand as RelayCommand)?.OnCanExecuteChanged();
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
            if (zipFiles.Length == 0)
            {
                TextBlock = "No archive files found to send.";
                IsLoading = false;
                return;
            }

            var latestZipFile = zipFiles.MaxBy(File.GetCreationTimeUtc);

            if (latestZipFile is null)
            {
                TextBlock = "Could not determine latest archive file.";
                IsLoading = false;
                return;
            }

            await _emailService.SendEmailAsync(
                "Log Analysis Recipient",
                UserEmail,
                "Log Analyzer For Windows - Logs",
                $"Please find the latest log archive attached ({Path.GetFileName(latestZipFile)}).",
                latestZipFile).ConfigureAwait(false);

            TextBlock = "Email sent successfully.";
        }
        catch (InvalidOperationException ioEx)
        {
            Debug.WriteLine($"Operation error sending email: {ioEx.Message}");
            TextBlock = ioEx.Message.Contains("Email service is not configured", StringComparison.Ordinal)
                ? "Error sending email: Email service is not configured. Please check settings."
                : $"Error sending email: An operation error occurred ({ioEx.Message})";
        }
        catch (SmtpException smtpEx)
        {
            Debug.WriteLine($"SMTP error sending email: {smtpEx.StatusCode} - {smtpEx.Message}");

            var userMessage = smtpEx.InnerException is SocketException
                ? "Error sending email: Network connection issue or email server unavailable. Please check your internet connection and server status."
                : smtpEx.StatusCode switch
                {
                    SmtpStatusCode.MailboxUnavailable =>
                        "Error sending email: Recipient mailbox unavailable or does not exist.",
                    SmtpStatusCode.ServiceNotAvailable =>
                        "Error sending email: Email service is temporarily unavailable. Please try again later.",
                    SmtpStatusCode.ClientNotPermitted or SmtpStatusCode.TransactionFailed =>
                        "Error sending email: Authentication failed or transaction rejected by the email server. Please check your email credentials and server policy.",
                    SmtpStatusCode.MustIssueStartTlsFirst =>
                        "Error sending email: Secure connection (TLS) required by the server was not established.",
                    _ => $"SMTP Error: {smtpEx.Message}"
                };

            TextBlock = userMessage;
        }
        catch (IOException fileEx)
        {
            Debug.WriteLine($"File error during email preparation: {fileEx.Message}");
            TextBlock = $"Error preparing email attachment: {fileEx.Message}";
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
            (OpenFolderCommand as RelayCommand)?.OnCanExecuteChanged();
            (ArchiveLatestFolderCommand as RelayCommand)?.OnCanExecuteChanged();
            (_sendEmailCommand as RelayCommand)?.OnCanExecuteChanged();
        });
    }

    private void OnMonitoringStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            (StartCommand as RelayCommand)?.OnCanExecuteChanged();
            (StopCommand as RelayCommand)?.OnCanExecuteChanged();

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
        Dispatcher.UIThread.InvokeAsync(() => OutputText += text + Environment.NewLine);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (_disposedValue) return;

        if (disposing)
        {
            try
            {
                if (_monitor.IsMonitoring)
                {
                    StopMonitoring();
                }

                _monitor.MonitoringStarted -= OnMonitoringStateChanged;
                _monitor.MonitoringStopped -= OnMonitoringStateChanged;

                _folderWatcher.Created -= OnLogDirectoryChanged;
                _folderWatcher.Deleted -= OnLogDirectoryChanged;
                _folderWatcher.Renamed -= OnLogDirectoryChanged;
                _folderWatcher.Changed -= OnLogDirectoryChanged;
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Dispose();

                _monitor.Dispose();
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"Object already disposed during cleanup: {ex.Message}");
            }
        }

        _disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
