using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls;
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

    private readonly HashSet<LogEntry>
        _processedLogs = new HashSet<LogEntry>();

    public AvaloniaList<string> LogLevels { get; } = new AvaloniaList<string>
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    public static Dictionary<string, string> LogLevelTranslations { get; private set; }

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
        set
        {
            if (SetProperty(ref _outputText, value))
            {
                CanSave = !string.IsNullOrEmpty(_outputText);
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
        set => SetProperty(ref _selectedFormat, value);
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
        // Initialize LogLevelTranslations based on current culture
        LogLevelTranslations = CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? LogLevelTranslationsHelper.LogLevelTranslationsRussian
            : LogLevelTranslationsHelper.LogLevelTranslationsEnglish;

        _emailService = new EmailService(); // Consider injecting via DI
        _fileSystemService = new FileSystemService(); // Consider injecting via DI

        _monitor = new LogMonitor();
        _monitor.MonitoringStarted += OnMonitoringStateChanged;
        _monitor.MonitoringStopped += OnMonitoringStateChanged;
        // _monitor.LogsChanged is subscribed in StartMonitoring

        StartCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
        StopCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);
        SaveCommand = new RelayCommand(SaveLogs, () => CanSave); // CanSave is already a property
        OpenFolderCommand = new RelayCommand(OpenLogFolder, () => IsFolderExists);
        ArchiveLatestFolderCommand = new RelayCommand(ArchiveLogFolder, () => IsFolderExists);


        // Ensure the directory for FileSystemWatcher exists or handle creation carefully
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(Path.Combine(documentsPath, "LogAnalyzerForWindows")))
        {
            Directory.CreateDirectory(Path.Combine(documentsPath, "LogAnalyzerForWindows"));
        }

        _folderWatcher = new FileSystemWatcher(documentsPath)
        {
            Filter = "LogAnalyzerForWindows", // This watches for changes *to the folder itself* if it's a direct child
            NotifyFilter =
                NotifyFilters.DirectoryName | NotifyFilters.FileName, // Watch for sub-directory or file changes
            IncludeSubdirectories = true // Watch subdirectories like the date folders
        };
        _folderWatcher.Created += OnLogDirectoryChanged;
        _folderWatcher.Deleted += OnLogDirectoryChanged;
        _folderWatcher.Renamed += OnLogDirectoryChanged; // Good to handle renames too
        _folderWatcher.Changed += OnLogDirectoryChanged; // For changes within files/folders if needed

        try
        {
            _folderWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enable FileSystemWatcher: {ex.Message}");
        }

        OnPropertyChanged(nameof(IsFolderExists));
    }

    private bool CanStartMonitoring() => !string.IsNullOrEmpty(SelectedLogLevel) &&
                                         !string.IsNullOrEmpty(SelectedTime) && !_monitor.IsMonitoring;

    private bool CanStopMonitoring() => _monitor.IsMonitoring;

    private void StartMonitoring()
    {
        if (!CanStartMonitoring()) return;

        IsLoading = true;
        TextBlock = "Starting monitoring...";
        OutputText = string.Empty; // Clear previous output
        _processedLogs.Clear();

        // This task will run the monitor loop.
        // We don't await it here because Monitor itself is a long-running blocking loop.
        Task.Run(() =>
        {
            ILogReader reader = new WindowsEventLogReader("System"); // Consider making "System" configurable
            // The analyzer for filtering by level is created inside _onLogsChangedHandler
            // The main analyzer passed to LogManager could be a general one or null if not needed there.
            LogAnalyzer generalAnalyzer = new LevelLogAnalyzer(SelectedLogLevel); // Example, or could be different
            ILogFormatter formatter = new LogFormatter(); // Used by TextBoxLogWriter
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
                        OnMonitoringStateChanged(); // Update button states
                    });
                    return; // Exit task
            }

            TimeFilter timeFilter = new TimeFilter(timeSpan);

            _onLogsChangedHandler = (incomingLogs) =>
            {
                var relevantLogs = timeFilter.Filter(incomingLogs);
                var levelAnalyzer = new LevelLogAnalyzer(
                    LogLevelTranslations.TryGetValue(SelectedLogLevel, out var translatedLevel)
                        ? translatedLevel
                        : SelectedLogLevel
                );

                var newUniqueLevelLogs = levelAnalyzer.FilterByLevel(relevantLogs)
                    .Where(log => _processedLogs.Add(log)) // HashSet.Add returns true if item was added
                    .ToList();

                if (newUniqueLevelLogs.Any())
                {
                    manager.ProcessLogs(newUniqueLevelLogs); // Process only new, unique, filtered logs for display
                    // Analyze all processed logs for the summary count
                    // generalAnalyzer.Analyze(_processedLogs); // This was writing to console, TextBlock is updated below
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        TextBlock =
                            $"Monitoring... Unique '{SelectedLogLevel}' logs found: {_processedLogs.Count(l => l.Level?.Equals(SelectedLogLevel, StringComparison.OrdinalIgnoreCase) ?? false)}";
                    });
                }
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
        if (!CanStopMonitoring()) return;

        TextBlock = "Stopping monitoring...";
        _monitor.StopMonitoring();

        if (_onLogsChangedHandler != null)
        {
            _monitor.LogsChanged -= _onLogsChangedHandler;
            _onLogsChangedHandler = null;
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
                return;
            }

            var latestZipFile = zipFiles.OrderByDescending(File.GetCreationTimeUtc).First();

            await _emailService.SendEmailAsync("Log Analysis Recipient", UserEmail, "Log Analyzer For Windows - Logs",
                $"Please find the latest log archive attached ({Path.GetFileName(latestZipFile)}).", latestZipFile);
            TextBlock = "Email sent successfully.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending email: {ex.Message}");
            TextBlock = $"Error sending email: {ex.Message}";
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
                IsLoading = false; // Assuming monitor started successfully, actual log processing will update IsLoading if needed
            }
            else // Monitoring stopped
            {
                TextBlock = "Monitoring stopped.";
                IsLoading = false;
                // Optionally reset selections when monitoring stops:
                // SelectedLogLevel = null;
                // SelectedTime = null;
                // _processedLogs.Clear(); // Already cleared in StartMonitoring, but could be here too if stop is not followed by start
                // OutputText = string.Empty; // Clear output on stop
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
                _monitor.MonitoringStarted -= OnMonitoringStateChanged;
                _monitor.MonitoringStopped -= OnMonitoringStateChanged;
                if (_onLogsChangedHandler != null)
                {
                    _monitor.LogsChanged -= _onLogsChangedHandler;
                }

                _monitor.StopMonitoring();

                _folderWatcher.Created -= OnLogDirectoryChanged;
                _folderWatcher.Deleted -= OnLogDirectoryChanged;
                _folderWatcher.Renamed -= OnLogDirectoryChanged;
                _folderWatcher.Changed -= OnLogDirectoryChanged;
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Dispose();
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