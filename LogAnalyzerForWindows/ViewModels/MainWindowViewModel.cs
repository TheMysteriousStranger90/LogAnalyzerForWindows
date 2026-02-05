using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Mail;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Database;
using LogAnalyzerForWindows.Database.Repositories;
using LogAnalyzerForWindows.Filter;
using LogAnalyzerForWindows.Formatter;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Analyzer;
using LogAnalyzerForWindows.Models.Reader;

namespace LogAnalyzerForWindows.ViewModels;

internal sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable, IDisposable
{
    private readonly IEmailService _emailService;
    private readonly IDialogService _dialogService;
    private readonly ILogExportService _exportService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogMonitor _monitor;
    private readonly ILogRepository _logRepository;
    private readonly ILogStatisticsService _statisticsService;
    private readonly FileSystemWatcher _folderWatcher;
    private readonly Func<ILogRepository, PaginationViewModel> _paginationViewModelFactory;
    private readonly LogFormatter _formatter;

    private string _selectedLogLevel = string.Empty;
    private string _selectedLogSource = string.Empty;
    private string _selectedTime = string.Empty;
    private ICommand? _sendEmailCommand;
    private EventHandler<LogsChangedEventArgs>? _onLogsChangedHandler;

    private readonly ConcurrentDictionary<LogEntry, byte> _processedLogs = new();
    private CancellationTokenSource? _processingCts;

    private DashboardViewModel? _dashboardViewModel;
    private string _textBlock = string.Empty;
    private string _outputText = string.Empty;
    private string _selectedFormat = "TXT";
    private bool _isLoading;
    private bool _canSave;
    private string _userEmail = string.Empty;
    private string _currentSessionId = string.Empty;
    private PaginationViewModel? _paginationViewModel;
    private bool _useDatabaseMode;
    private AvaloniaList<string> _availableSessions = new();
    private string? _selectedSession;
    private bool _hasDatabaseRecords;

    public bool IsMonitoring => _monitor.IsMonitoring;

    public DashboardViewModel? DashboardViewModel
    {
        get => _dashboardViewModel;
        private set => SetProperty(ref _dashboardViewModel, value);
    }

    public AvaloniaList<string> LogSources { get; } = new();
    public AvaloniaList<string> LogLevels { get; private set; } = new();
    public AvaloniaList<string> Times { get; } = ["Last hour", "Last 24 hours", "Last 3 days", "Last 7 days"];
    public AvaloniaList<string> Formats { get; } = ["TXT", "JSON"];

    public string TextBlock
    {
        get => _textBlock;
        set => SetProperty(ref _textBlock, value);
    }

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
                _ = LoadAvailableLevelsForSourceAsync();
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

    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                UpdateCanSaveState();
                (ExportSessionCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool CanSave
    {
        get => _canSave;
        private set => SetProperty(ref _canSave, value);
    }

    public string UserEmail
    {
        get => _userEmail;
        set
        {
            if (SetProperty(ref _userEmail, value))
            {
                (_sendEmailCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    private static string DefaultLogFolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AzioEventLogAnalyzer");

    public static bool IsFolderExists => Directory.Exists(DefaultLogFolderPath);

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    private readonly ISettingsService _settingsService;
    public ICommand OpenSettingsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ArchiveLatestFolderCommand { get; }
    public ICommand ExportSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }

    public ICommand SendEmailCommand => _sendEmailCommand ??= new AsyncRelayCommand(
        SendEmailAsync,
        CanSendEmail
    );

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
                (ExportSessionCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
                (DeleteSessionCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public bool HasDatabaseRecords
    {
        get => _hasDatabaseRecords;
        private set
        {
            if (SetProperty(ref _hasDatabaseRecords, value))
            {
                (ClearHistoryCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public ICommand ViewHistoryCommand { get; }
    public ICommand ClearHistoryCommand { get; }

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

                (StartCommand as RelayCommand)?.OnCanExecuteChanged();
                (StopCommand as RelayCommand)?.OnCanExecuteChanged();
                (ClearHistoryCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public bool CanToggleDatabaseMode => !_monitor.IsMonitoring;

    public MainWindowViewModel(
        IEmailService emailService,
        IFileSystemService fileSystemService,
        ILogMonitor monitor,
        ILogRepository logRepository,
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogStatisticsService statisticsService,
        ILogExportService exportService,
        Func<ILogRepository, PaginationViewModel> paginationViewModelFactory)
    {
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));
        _paginationViewModelFactory = paginationViewModelFactory ??
                                      throw new ArgumentNullException(nameof(paginationViewModelFactory));
        _formatter = new LogFormatter();

        _monitor.MonitoringStarted += OnMonitoringStateChanged;
        _monitor.MonitoringStopped += OnMonitoringStateChanged;

        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        StartCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
        StopCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);
        SaveCommand = new AsyncRelayCommand(SaveLogsAsync, () => CanSave);
        OpenFolderCommand = new RelayCommand(OpenLogFolder, () => IsFolderExists);
        ArchiveLatestFolderCommand = new AsyncRelayCommand(ArchiveLogFolderAsync, () => IsFolderExists);
        ExportSessionCommand = new AsyncRelayCommand(ExportSessionLogsAsync, CanExportSession);
        DeleteSessionCommand = new AsyncRelayCommand(DeleteSessionAsync, CanDeleteSession);
        ViewHistoryCommand = new AsyncRelayCommand(ViewHistoryAsync);
        ClearHistoryCommand = new AsyncRelayCommand(ClearOldHistoryAsync, CanClearHistory);

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var logPath = Path.Combine(documentsPath, "AzioEventLogAnalyzer");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
        }

        _folderWatcher = new FileSystemWatcher(documentsPath)
        {
            Filter = "AzioEventLogAnalyzer",
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

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await InitializeDatabaseAsync().ConfigureAwait(false);
            await CheckDatabaseRecordsAsync().ConfigureAwait(false);
            await LoadAvailableLogSourcesAsync().ConfigureAwait(false);

            DashboardViewModel = new DashboardViewModel(_statisticsService);
            await DashboardViewModel.LoadSessionsAsync().ConfigureAwait(false);
            await DashboardViewModel.LoadDashboardDataAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initialization error: {ex.Message}");
        }
    }

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

    [SupportedOSPlatform("windows")]
    private async Task LoadAvailableLogSourcesAsync()
    {
        try
        {
            var sources = await WindowsEventLogReader.GetAvailableLogNamesAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
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
    }

    [SupportedOSPlatform("windows")]
    private async Task LoadAvailableLevelsForSourceAsync()
    {
        if (string.IsNullOrEmpty(SelectedLogSource))
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TextBlock = $"Loading available levels for {SelectedLogSource}...";
            IsLoading = true;
        });

        try
        {
            var levels = await WindowsEventLogReader.GetAvailableLevelsForLogAsync(SelectedLogSource)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
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
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Debug.WriteLine($"Error loading levels for {SelectedLogSource}: {ex.Message}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TextBlock = $"Error loading levels: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    private static async Task InitializeDatabaseAsync()
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
        PaginationViewModel = _paginationViewModelFactory(_logRepository);
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

    private bool CanClearHistory()
    {
        return UseDatabaseMode && HasDatabaseRecords;
    }

    private void UpdateCanSaveState()
    {
        CanSave = !_processedLogs.IsEmpty && !string.IsNullOrEmpty(SelectedFormat);
        (SaveCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
    }

    private bool CanStartMonitoring() =>
        !UseDatabaseMode &&
        !string.IsNullOrEmpty(SelectedLogSource) &&
        !string.IsNullOrEmpty(SelectedLogLevel) &&
        !string.IsNullOrEmpty(SelectedTime) &&
        !_monitor.IsMonitoring;

    private bool CanStopMonitoring() => !UseDatabaseMode && _monitor.IsMonitoring;

    [SupportedOSPlatform("windows")]
    private void StartMonitoring()
    {
        if (!CanStartMonitoring()) return;

        IsLoading = true;
        TextBlock = "Starting monitoring...";
        OutputText = string.Empty;
        _processedLogs.Clear();
        UpdateCanSaveState();

        _processingCts?.Cancel();
        _processingCts?.Dispose();
        _processingCts = new CancellationTokenSource();

        _currentSessionId = $"Session_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{SelectedLogSource}";

        var reader = new WindowsEventLogReader(SelectedLogSource);
        var levelAnalyzer = new LevelLogAnalyzer(SelectedLogLevel);

        var timeSpan = SelectedTime switch
        {
            "Last hour" => TimeSpan.FromHours(1),
            "Last 24 hours" => TimeSpan.FromDays(1),
            "Last 3 days" => TimeSpan.FromDays(3),
            "Last 7 days" => TimeSpan.FromDays(7),
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
            if (_processingCts == null || _processingCts.IsCancellationRequested)
                return;

            var relevantLogs = timeFilter.Filter(args.Logs);

            var newUniqueLevelLogs = levelAnalyzer.FilterByLevel(relevantLogs)
                .Where(log => _processedLogs.TryAdd(log, 0))
                .OrderBy(log => log.Timestamp)
                .ToList();

            if (newUniqueLevelLogs.Count > 0)
            {
                _ = SaveLogsToDatabaseAsync(newUniqueLevelLogs);

                const int uiBatchSize = 50;
                for (int i = 0; i < newUniqueLevelLogs.Count; i += uiBatchSize)
                {
                    if (_processingCts.IsCancellationRequested)
                        break;

                    var batch = newUniqueLevelLogs.Skip(i).Take(uiBatchSize).ToList();
                    var batchIndex = i;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateCanSaveState();

                        foreach (var log in batch)
                        {
                            var formattedLog = _formatter.Format(log);
                            OutputText += formattedLog + Environment.NewLine;
                        }

                        var matchingCount = _processedLogs.Count(l =>
                            string.Equals(l.Key.Level, SelectedLogLevel, StringComparison.OrdinalIgnoreCase));
                        TextBlock =
                            $"Monitoring {SelectedLogSource}... '{SelectedLogLevel}' logs: {matchingCount} " +
                            $"(Processing batch {batchIndex / uiBatchSize + 1}/{(newUniqueLevelLogs.Count + uiBatchSize - 1) / uiBatchSize})";
                    });

                    try
                    {
                        await Task.Delay(10, _processingCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var matchingCount = _processedLogs.Count(l =>
                        string.Equals(l.Key.Level, SelectedLogLevel, StringComparison.OrdinalIgnoreCase));
                    TextBlock =
                        $"Monitoring {SelectedLogSource}... Unique '{SelectedLogLevel}' logs found: {matchingCount} (Session: {_currentSessionId})";
                });
            }
        };

        _monitor.LogsChanged += _onLogsChangedHandler;
        _monitor.Monitor(reader);
    }

    private async Task SaveLogsToDatabaseAsync(List<LogEntry> logs)
    {
        try
        {
            await _logRepository.SaveLogsAsync(logs, _currentSessionId).ConfigureAwait(false);
            Debug.WriteLine($"Bulk saved {logs.Count} logs to database");

            _statisticsService.InvalidateCache(_currentSessionId);

            await CheckDatabaseRecordsAsync().ConfigureAwait(false);

            if (DashboardViewModel != null)
            {
                await DashboardViewModel.LoadSessionsAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Error saving logs to database: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error saving logs to database: {ex.Message}");
        }
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
            TextBlock = "Clearing all history...";
            IsLoading = true;
        });

        try
        {
            var deletedCount = await _logRepository.ClearAllLogsAsync().ConfigureAwait(false);

            _statisticsService.InvalidateCache();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TextBlock = $"Deleted {deletedCount} log entries. Database cleared.";
            });

            await CheckDatabaseRecordsAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (UseDatabaseMode && PaginationViewModel != null)
                {
                    await PaginationViewModel.LoadLogsAsync().ConfigureAwait(false);
                }

                if (DashboardViewModel != null)
                {
                    await DashboardViewModel.LoadSessionsAsync().ConfigureAwait(false);
                    await DashboardViewModel.LoadDashboardDataAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Error clearing history: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => { TextBlock = $"Error clearing history: {ex.Message}"; });
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error clearing history: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => { TextBlock = $"IO error clearing history: {ex.Message}"; });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { IsLoading = false; });
        }
    }

    private void StopMonitoring()
    {
        TextBlock = "Stopping monitoring...";

        try
        {
            _processingCts?.Cancel();

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

    private async Task SaveLogsAsync()
    {
        if (string.IsNullOrEmpty(SelectedFormat) || _processedLogs.IsEmpty)
        {
            TextBlock = "No logs to save or format not selected.";
            return;
        }

        TextBlock = "Saving logs...";
        IsLoading = true;

        try
        {
            var filePath = await _exportService.ExportLogsAsync(
                _processedLogs.Keys,
                SelectedFormat
            ).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = $"Logs saved to: {filePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine($"Error saving logs: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = $"Error saving logs: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private void OpenLogFolder()
    {
        _fileSystemService.OpenFolder(DefaultLogFolderPath, UpdateTextBlockOnUiThread);
    }

    private async Task ArchiveLogFolderAsync()
    {
        TextBlock = "Archiving...";
        IsLoading = true;

        await Task.Run(() =>
        {
            _fileSystemService.ArchiveLatestFolder(DefaultLogFolderPath, UpdateTextBlockOnUiThread);
        }).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = false;
            (_sendEmailCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
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
                "AzioEventLogAnalyzer - Logs",
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
                ? "Error sending email: Network connection issue or email server unavailable."
                : smtpEx.StatusCode switch
                {
                    SmtpStatusCode.MailboxUnavailable =>
                        "Error sending email: Recipient mailbox unavailable or does not exist.",
                    SmtpStatusCode.ServiceNotAvailable =>
                        "Error sending email: Email service is temporarily unavailable.",
                    SmtpStatusCode.ClientNotPermitted or SmtpStatusCode.TransactionFailed =>
                        "Error sending email: Authentication failed or transaction rejected.",
                    SmtpStatusCode.MustIssueStartTlsFirst =>
                        "Error sending email: Secure connection (TLS) required but not established.",
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
            (ArchiveLatestFolderCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            (_sendEmailCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
        });
    }

    private async void OnMonitoringStateChanged(object? sender, EventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            (StartCommand as RelayCommand)?.OnCanExecuteChanged();
            (StopCommand as RelayCommand)?.OnCanExecuteChanged();

            OnPropertyChanged(nameof(CanToggleDatabaseMode));
            OnPropertyChanged(nameof(IsMonitoring));

            if (_monitor.IsMonitoring)
            {
                TextBlock = "Monitoring started.";
                IsLoading = false;
            }
            else
            {
                TextBlock = "Monitoring stopped.";
                IsLoading = false;

                _statisticsService.InvalidateCache();

                if (DashboardViewModel != null)
                {
                    await DashboardViewModel.LoadSessionsAsync().ConfigureAwait(false);
                    await DashboardViewModel.LoadDashboardDataAsync().ConfigureAwait(false);
                }
            }
        });
    }

    private bool CanExportSession()
    {
        return UseDatabaseMode &&
               !string.IsNullOrEmpty(SelectedSession) &&
               SelectedSession != "All Sessions" &&
               !string.IsNullOrEmpty(SelectedFormat);
    }

    private async Task OpenSettingsAsync()
    {
        var settingsVm = new SettingsViewModel(_settingsService, _emailService);
        await _dialogService.ShowSettingsDialogAsync(settingsVm).ConfigureAwait(false);
    }

    private async Task ExportSessionLogsAsync()
    {
        if (string.IsNullOrEmpty(SelectedSession) || SelectedSession == "All Sessions")
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = "Please select a specific session to export.");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = true;
            TextBlock = $"Exporting session '{SelectedSession}'...";
        });

        try
        {
            var allLogs = await LoadAllSessionLogsAsync(SelectedSession).ConfigureAwait(false);

            if (allLogs.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock = "No logs found in the selected session.";
                    IsLoading = false;
                });
                return;
            }

            var filePath = await _exportService.ExportLogsAsync(
                allLogs,
                SelectedFormat,
                SelectedSession
            ).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = $"Exported {allLogs.Count} logs from session '{SelectedSession}' to: {filePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Debug.WriteLine($"Error exporting session logs: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = $"Error exporting logs: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private async Task<List<LogEntry>> LoadAllSessionLogsAsync(string sessionId)
    {
        var allLogs = new List<LogEntry>();
        const int pageSize = 1000;
        var currentPage = 1;
        int totalCount;

        do
        {
            var (logs, count) = await _logRepository.GetLogsAsync(
                currentPage,
                pageSize,
                levelFilter: null,
                startDate: null,
                endDate: null,
                sessionId: sessionId
            ).ConfigureAwait(false);

            allLogs.AddRange(logs);
            totalCount = count;
            currentPage++;

            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = $"Loading session logs... {allLogs.Count}/{totalCount}");
        } while (allLogs.Count < totalCount);

        return allLogs;
    }

    private bool CanDeleteSession()
    {
        return UseDatabaseMode &&
               !string.IsNullOrEmpty(SelectedSession) &&
               SelectedSession != "All Sessions";
    }

    private async Task DeleteSessionAsync()
    {
        if (string.IsNullOrEmpty(SelectedSession) || SelectedSession == "All Sessions")
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = "Please select a specific session to delete.");
            return;
        }

        var sessionToDelete = SelectedSession;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = true;
            TextBlock = $"Deleting session '{sessionToDelete}'...";
        });

        try
        {
            var deletedCount = await _logRepository.DeleteSessionAsync(sessionToDelete).ConfigureAwait(false);

            _statisticsService.InvalidateCache(sessionToDelete);
            _statisticsService.InvalidateCache();

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                TextBlock = $"Deleted {deletedCount} logs from session '{sessionToDelete}'.";

                AvailableSessions.Remove(sessionToDelete);
                SelectedSession = "All Sessions";

                if (PaginationViewModel != null)
                {
                    await PaginationViewModel.LoadLogsAsync().ConfigureAwait(false);
                }

                if (DashboardViewModel != null)
                {
                    await DashboardViewModel.LoadSessionsAsync().ConfigureAwait(false);
                    await DashboardViewModel.LoadDashboardDataAsync().ConfigureAwait(false);
                }
            });

            await CheckDatabaseRecordsAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Debug.WriteLine($"Error deleting session: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                TextBlock = $"Error deleting session: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    private void UpdateTextBlockOnUiThread(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => TextBlock = message);
    }

    private bool _disposedValue;

    public async ValueTask DisposeAsync()
    {
        if (_disposedValue) return;

        try
        {
            if (_monitor.IsMonitoring)
            {
                StopMonitoring();
            }

            if (_processingCts != null)
            {
                await _processingCts.CancelAsync().ConfigureAwait(false);
                _processingCts.Dispose();
            }

            _monitor.MonitoringStarted -= OnMonitoringStateChanged;
            _monitor.MonitoringStopped -= OnMonitoringStateChanged;

            _folderWatcher.Created -= OnLogDirectoryChanged;
            _folderWatcher.Deleted -= OnLogDirectoryChanged;
            _folderWatcher.Renamed -= OnLogDirectoryChanged;
            _folderWatcher.Changed -= OnLogDirectoryChanged;
            _folderWatcher.EnableRaisingEvents = false;
            _folderWatcher.Dispose();

            if (_monitor is IDisposable disposableMonitor)
            {
                disposableMonitor.Dispose();
            }

            if (DashboardViewModel != null)
            {
                await DashboardViewModel.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException ex)
        {
            Debug.WriteLine($"Object already disposed during cleanup: {ex.Message}");
        }

        _disposedValue = true;
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposedValue) return;

        try
        {
            if (_monitor.IsMonitoring)
            {
                StopMonitoring();
            }

            _processingCts?.Cancel();
            _processingCts?.Dispose();

            _monitor.MonitoringStarted -= OnMonitoringStateChanged;
            _monitor.MonitoringStopped -= OnMonitoringStateChanged;

            _folderWatcher.Created -= OnLogDirectoryChanged;
            _folderWatcher.Deleted -= OnLogDirectoryChanged;
            _folderWatcher.Renamed -= OnLogDirectoryChanged;
            _folderWatcher.Changed -= OnLogDirectoryChanged;
            _folderWatcher.EnableRaisingEvents = false;
            _folderWatcher.Dispose();

            if (_monitor is IDisposable disposableMonitor)
            {
                disposableMonitor.Dispose();
            }
        }
        catch (ObjectDisposedException ex)
        {
            Debug.WriteLine($"Object already disposed during cleanup: {ex.Message}");
        }

        _disposedValue = true;
        GC.SuppressFinalize(this);
    }
}
