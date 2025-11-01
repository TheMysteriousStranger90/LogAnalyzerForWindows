using System.ComponentModel;
using System.Diagnostics;
using System.Net.Mail;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Database;
using LogAnalyzerForWindows.Database.Repositories;
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
    private string _selectedLogSource = string.Empty;
    private string _selectedTime = string.Empty;
    private ICommand? _startCommand;
    private ICommand? _stopCommand;
    private ICommand? _sendEmailCommand;
    private EventHandler<LogsChangedEventArgs>? _onLogsChangedHandler;

    private readonly HashSet<LogEntry> _processedLogs = [];

    public AvaloniaList<string> LogSources { get; } = new();
    public AvaloniaList<string> LogLevels { get; private set; } = new();
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

    public string SelectedLogSource
    {
        get => _selectedLogSource;
        set
        {
            if (SetProperty(ref _selectedLogSource, value))
            {
                LoadAvailableLevelsForSource();
                (StartCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }
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

    private readonly LogRepository _logRepository;
    private string _currentSessionId = string.Empty;
    private PaginationViewModel? _paginationViewModel;
    private bool _useDatabaseMode;
    private AvaloniaList<string> _availableSessions = new();
    private string? _selectedSession;

    public PaginationViewModel? PaginationViewModel
    {
        get => _paginationViewModel;
        private set => SetProperty(ref _paginationViewModel, value);
    }

    public AvaloniaList<string> AvailableSessions
    {
        get => _availableSessions;
        private set => SetProperty(ref _availableSessions, value);
    }

    public string? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                ApplySessionFilter();
            }
        }
    }

    private bool _hasDatabaseRecords;

    public bool HasDatabaseRecords
    {
        get => _hasDatabaseRecords;
        private set
        {
            if (SetProperty(ref _hasDatabaseRecords, value))
            {
                (ClearHistoryCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public ICommand ViewHistoryCommand { get; }
    public ICommand ClearHistoryCommand { get; }

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

        _logRepository = new LogRepository();

        ViewHistoryCommand = new RelayCommand(async () => await ViewHistoryAsync().ConfigureAwait(false));

        ClearHistoryCommand = new RelayCommand(
            async () => await ClearOldHistoryAsync().ConfigureAwait(false),
            CanClearHistory
        );

        InitializeDatabaseAsync();
        _ = CheckDatabaseRecordsAsync();

        LoadAvailableLogSources();
    }

    [SupportedOSPlatform("windows")]
    private void LoadAvailableLogSources()
    {
        Task.Run(() =>
        {
            try
            {
                var sources = WindowsEventLogReader.GetAvailableLogNames();

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LogSources.Clear();
                    foreach (var source in sources)
                    {
                        LogSources.Add(source);
                    }

                    if (LogSources.Contains("System"))
                    {
                        SelectedLogSource = "System";
                    }
                    else if (LogSources.Count > 0)
                    {
                        SelectedLogSource = LogSources[0];
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Invalid operation while loading log sources: {ex.Message}");
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO error while loading log sources: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error loading log sources: {ex.Message}");
                throw;
            }
        });
    }

    [SupportedOSPlatform("windows")]
    private void LoadAvailableLevelsForSource()
    {
        if (string.IsNullOrEmpty(SelectedLogSource))
        {
            return;
        }

        TextBlock = $"Loading available levels for {SelectedLogSource}...";
        IsLoading = true;

        Task.Run(() =>
        {
            try
            {
                var levels = WindowsEventLogReader.GetAvailableLevelsForLog(SelectedLogSource);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var previousSelection = SelectedLogLevel;

                    LogLevels.Clear();
                    foreach (var level in levels)
                    {
                        LogLevels.Add(level);
                    }

                    if (!string.IsNullOrEmpty(previousSelection) && LogLevels.Contains(previousSelection))
                    {
                        SelectedLogLevel = previousSelection;
                    }
                    else if (LogLevels.Count > 0)
                    {
                        SelectedLogLevel = LogLevels[0];
                    }

                    TextBlock = $"Available levels loaded for {SelectedLogSource}";
                    IsLoading = false;
                });
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Invalid operation while loading levels for {SelectedLogSource}: {ex.Message}");

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = $"Error loading levels: {ex.Message}";
                    IsLoading = false;
                });
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO error while loading levels for {SelectedLogSource}: {ex.Message}");

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = $"Error loading levels: {ex.Message}";
                    IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error loading levels for {SelectedLogSource}: {ex.Message}");
                throw;
            }
        });
    }

    private static async void InitializeDatabaseAsync()
    {
        try
        {
            using var context = new LogAnalyzerDbContext();
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            Debug.WriteLine("Database initialized successfully");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Database initialization error: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error during database initialization: {ex.Message}");
        }
    }

    private void InitializeDatabaseMode()
    {
        PaginationViewModel = new PaginationViewModel(_logRepository);
        _ = LoadSessionsAsync();
        _ = PaginationViewModel.LoadLogsAsync();
    }

    private async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _logRepository.GetSessionIdsAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableSessions.Clear();
                AvailableSessions.Add("All Sessions");
                foreach (var session in sessions)
                {
                    AvailableSessions.Add(session);
                }

                SelectedSession = "All Sessions";
            });
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Error loading sessions: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error loading sessions: {ex.Message}");
        }
    }

    private void ApplySessionFilter()
    {
        if (PaginationViewModel == null) return;

        var sessionFilter = SelectedSession == "All Sessions" ? null : SelectedSession;
        PaginationViewModel.SetFilters(sessionId: sessionFilter);
    }

    private async Task CheckDatabaseRecordsAsync()
    {
        try
        {
            var stats = await _logRepository.GetLogStatisticsAsync().ConfigureAwait(false);
            var totalCount = stats.Values.Sum();
            var hasRecords = totalCount > 0;

            await Dispatcher.UIThread.InvokeAsync(() => { HasDatabaseRecords = hasRecords; });
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Invalid operation while checking database records: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => { HasDatabaseRecords = false; });
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error while checking database records: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => { HasDatabaseRecords = false; });
        }
    }

    public bool UseDatabaseMode
    {
        get => _useDatabaseMode;
        set
        {
            if (SetProperty(ref _useDatabaseMode, value))
            {
                if (value)
                {
                    InitializeDatabaseMode();
                }
                else
                {
                    PaginationViewModel = null;
                }

                (ClearHistoryCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public bool CanToggleDatabaseMode => !_monitor.IsMonitoring;

    private bool CanClearHistory()
    {
        return UseDatabaseMode && HasDatabaseRecords;
    }

    private void UpdateCanSaveState()
    {
        CanSave = _processedLogs.Count != 0 && !string.IsNullOrEmpty(SelectedFormat);
        (SaveCommand as RelayCommand)?.OnCanExecuteChanged();
    }

    private bool CanStartMonitoring() =>
        !string.IsNullOrEmpty(SelectedLogSource) &&
        !string.IsNullOrEmpty(SelectedLogLevel) &&
        !string.IsNullOrEmpty(SelectedTime) &&
        !_monitor.IsMonitoring;

    private bool CanStopMonitoring() => _monitor.IsMonitoring;

    [SupportedOSPlatform("windows")]
    private void StartMonitoring()
    {
        if (!CanStartMonitoring()) return;

        IsLoading = true;
        TextBlock = "Starting monitoring...";
        OutputText = string.Empty;
        _processedLogs.Clear();
        UpdateCanSaveState();

        _currentSessionId = $"Session_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SelectedLogSource}";

        ILogReader reader = new WindowsEventLogReader(SelectedLogSource);
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

        _onLogsChangedHandler = async (sender, args) =>
        {
            var incomingLogs = args.Logs;
            var relevantLogs = timeFilter.Filter(incomingLogs);
            var levelAnalyzer = new LevelLogAnalyzer(SelectedLogLevel);

            var newUniqueLevelLogs = levelAnalyzer.FilterByLevel(relevantLogs)
                .Where(_processedLogs.Add)
                .ToList();

            if (newUniqueLevelLogs.Count > 0)
            {
                try
                {
                    await _logRepository.SaveLogsAsync(newUniqueLevelLogs, _currentSessionId).ConfigureAwait(false);
                    Debug.WriteLine($"Saved {newUniqueLevelLogs.Count} logs to database");

                    await CheckDatabaseRecordsAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine($"Error saving logs to database: {ex.Message}");
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"IO error saving logs to database: {ex.Message}");
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateCanSaveState();
                    manager.ProcessLogs(newUniqueLevelLogs);
                    var matchingCount = _processedLogs.Count(l =>
                        string.Equals(l.Level, SelectedLogLevel, StringComparison.OrdinalIgnoreCase));
                    TextBlock =
                        $"Monitoring {SelectedLogSource}... Unique '{SelectedLogLevel}' logs found: {matchingCount} (Session: {_currentSessionId})";
                });
            }
        };

        _monitor.LogsChanged += _onLogsChangedHandler;
        _monitor.Monitor(reader);
    }

    private async Task ViewHistoryAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TextBlock = "Loading history from database...";
            IsLoading = true;
        });

        try
        {
            var sessions = await _logRepository.GetSessionIdsAsync().ConfigureAwait(false);

            if (sessions.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = "No history found in database.";
                    HasDatabaseRecords = false;
                    IsLoading = false;
                });
                return;
            }

            var stats = await _logRepository.GetLogStatisticsAsync().ConfigureAwait(false);
            var statsText = string.Join(", ", stats.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            var totalCount = stats.Values.Sum();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UseDatabaseMode = true;
                HasDatabaseRecords = totalCount > 0;
                TextBlock = $"History loaded. Total sessions: {sessions.Count}. Statistics: {statsText}";
            });

            if (PaginationViewModel != null)
            {
                await PaginationViewModel.LoadLogsAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Error loading history: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TextBlock = $"Error loading history: {ex.Message}";
                IsLoading = false;
            });
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error loading history: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TextBlock = $"IO error loading history: {ex.Message}";
                IsLoading = false;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { IsLoading = false; });
        }
    }

    private async Task ClearOldHistoryAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TextBlock = "Clearing old history...";
            IsLoading = true;
        });

        await Task.Run(async () =>
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-30);
                var deletedCount = await _logRepository.DeleteOldLogsAsync(cutoffDate).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = $"Deleted {deletedCount} old log entries (older than 30 days).";
                });

                await CheckDatabaseRecordsAsync().ConfigureAwait(false);

                var currentUseDatabaseMode = false;
                PaginationViewModel? currentPaginationViewModel = null;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentUseDatabaseMode = UseDatabaseMode;
                    currentPaginationViewModel = PaginationViewModel;
                });

                if (currentUseDatabaseMode && currentPaginationViewModel != null)
                {
                    await currentPaginationViewModel.LoadLogsAsync().ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Error clearing history: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() => { TextBlock = $"Error clearing history: {ex.Message}"; });
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO error clearing history: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = $"IO error clearing history: {ex.Message}";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => { IsLoading = false; });
            }
        }).ConfigureAwait(false);
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

            OnPropertyChanged(nameof(CanToggleDatabaseMode));

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
