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

public sealed class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    private readonly IEmailService _emailService;
    private readonly IFileSystemService _fileSystemService;

    private string _selectedLogLevel;
    private string _selectedTime;
    private ICommand _startCommand;
    private ICommand _stopCommand;
    private ICommand _sendEmailCommand;
    public ICommand SaveCommand => new RelayCommand(Save);
    public ICommand OpenFolderCommand => new RelayCommand(OpenFolder);
    public ICommand ArchiveLatestFolderCommand => new RelayCommand(ArchiveLatestFolder);
    private Action<IEnumerable<LogEntry>> _onLogsChanged;
    private FileSystemWatcher _folderWatcher;

    public AvaloniaList<string> LogLevels { get; } = new AvaloniaList<string>
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    public static Dictionary<string, string> LogLevelTranslations;

    public AvaloniaList<string> Times { get; } =
        new AvaloniaList<string> { "Last hour", "Last 24 hours", "Last 3 days" };

    public AvaloniaList<string> Formats { get; } = new AvaloniaList<string> { "txt", "json" };

    private string _textBlock;

    public string TextBlock
    {
        get { return _textBlock; }
        set
        {
            if (value != _textBlock)
            {
                _textBlock = value;
                OnPropertyChanged();
            }
        }
    }

    private string _outputText;

    public string OutputText
    {
        get { return _outputText; }
        set
        {
            if (value != _outputText)
            {
                _outputText = value;
                OnPropertyChanged(nameof(OutputText));
                CanSave = !string.IsNullOrEmpty(_outputText);
            }
        }
    }

    public string SelectedLogLevel
    {
        get { return _selectedLogLevel; }
        set
        {
            if (value != _selectedLogLevel)
            {
                _selectedLogLevel = value;
                OnPropertyChanged();
                ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedTime
    {
        get { return _selectedTime; }
        set
        {
            if (value != _selectedTime)
            {
                _selectedTime = value;
                OnPropertyChanged();
                ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isLoading;

    public bool IsLoading
    {
        get { return _isLoading; }
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    private bool _canSave;

    public bool CanSave
    {
        get { return _canSave; }
        set
        {
            if (value != _canSave)
            {
                _canSave = value;
                OnPropertyChanged();
            }
        }
    }

    private string _userEmail;

    public string UserEmail
    {
        get { return _userEmail; }
        set
        {
            _userEmail = value;
            OnPropertyChanged();
            ((RelayCommand)_sendEmailCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsFolderExists
    {
        get
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LogAnalyzerForWindows");
            return Directory.Exists(defaultPath);
        }
    }

    public ICommand StartCommand
    {
        get { return _startCommand; }
        set
        {
            if (value != _startCommand)
            {
                _startCommand = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand StopCommand
    {
        get { return _stopCommand; }
        set
        {
            if (value != _stopCommand)
            {
                _stopCommand = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand SendEmailCommand
    {
        get
        {
            return _sendEmailCommand ?? (_sendEmailCommand = new RelayCommand(
                async () => await SendEmailAsync(),
                () => EmailSender.IsValidEmail(UserEmail) && Directory.GetFiles(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "LogAnalyzerForWindows"), "*.zip").Length > 0));
        }
    }

    public string SelectedFormat { get; set; }

    private LogMonitor _monitor;

    public MainWindowViewModel()
    {
        if (CultureInfo.CurrentCulture.Name.StartsWith("ru"))
        {
            LogLevelTranslations = LogLevelTranslationsHelper.LogLevelTranslationsRussian;
        }
        else
        {
            LogLevelTranslations = LogLevelTranslationsHelper.LogLevelTranslationsEnglish;
        }

        _emailService = new EmailService();
        _fileSystemService = new FileSystemService();

        _monitor = new LogMonitor();

        _monitor.MonitoringStarted += OnMonitoringStartedOrStopped;
        _monitor.MonitoringStopped += OnMonitoringStartedOrStopped;

        StartCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
        StopCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);

        _folderWatcher = new FileSystemWatcher(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        {
            Filter = "LogAnalyzerForWindows",
            NotifyFilter = NotifyFilters.DirectoryName
        };
        _folderWatcher.Created += OnFolderChanged;
        _folderWatcher.Deleted += OnFolderChanged;
        _folderWatcher.EnableRaisingEvents = true;
    }

    private bool CanStartMonitoring()
    {
        return SelectedLogLevel != null && SelectedTime != null;
    }

    private bool CanStopMonitoring()
    {
        return _monitor.IsMonitoring;
    }

    private void StartMonitoring()
    {
        Task.Run(async () =>
        {
            IsLoading = true;
            TextBlock = "Starting monitoring...";

            ILogReader reader = new WindowsEventLogReader("System");
            LogAnalyzer analyzer = new LevelLogAnalyzer(SelectedLogLevel);
            ILogFormatter formatter = new LogFormatter();
            ILogWriter writer = new TextBoxLogWriter(formatter, UpdateOutputText);

            LogManager manager = new LogManager(reader, analyzer, formatter, writer);

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
                    throw new InvalidOperationException($"Unknown time interval: {SelectedTime}");
            }

            TimeFilter filter = new TimeFilter(timeSpan);

            HashSet<LogEntry> uniqueLogs = new HashSet<LogEntry>();

            if (_onLogsChanged == null)
            {
                _onLogsChanged = (logs) =>
                {
                    var filteredLogs = filter.Filter(logs);
                    LevelLogAnalyzer analyzer = new LevelLogAnalyzer(
                        SelectedLogLevel != null && LogLevelTranslations.ContainsKey(SelectedLogLevel)
                            ? LogLevelTranslations[SelectedLogLevel]
                            : SelectedLogLevel);
                    var levelLogs = analyzer.FilterByLevel(filteredLogs);
                    LogManager manager = new LogManager(reader, analyzer, formatter, writer);
                    foreach (var log in levelLogs)
                    {
                        string pattern = @"^(\d{2}\.\d{2}\.\d{4} \d{1,2}:\d{2}:\d{2})(?:\s+\1)+";

                        log.Message = Regex.Replace(log.Message, pattern, "$1");
                        uniqueLogs.Add(log);
                    }

                    manager.ProcessLogs(uniqueLogs);

                    analyzer.Analyze(uniqueLogs);
                    TextBlock = $"Number of unique logs: {uniqueLogs.Count}";
                };
            }

            _monitor.LogsChanged += _onLogsChanged;

            await Task.Run(() => { _monitor.Monitor(reader); });

            IsLoading = false;
            TextBlock = "Monitoring started.";

            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
        });
    }

    private void StopMonitoring()
    {
        _monitor.StopMonitoring();
        TextBlock = "Monitoring stopped.";

        if (_onLogsChanged != null)
        {
            _monitor.LogsChanged -= _onLogsChanged;
            _onLogsChanged = null;
        }

        SelectedLogLevel = null;
        SelectedTime = null;
        IsLoading = false;

        ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
    }

    private void Save()
    {
        try
        {
            if (string.IsNullOrEmpty(SelectedFormat))
            {
                return;
            }

            ILogFormatter formatter;
            switch (SelectedFormat)
            {
                case "txt":
                    formatter = new LogFormatter();
                    break;
                case "json":
                    formatter = new JsonLogFormatter();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown format: {SelectedFormat}");
            }

            var logEntries = OutputText.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var match = Regex.Match(line,
                        @"^(?<timestamp>\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}) (?<level>\w+) (?<message>.+)$");
                    if (match.Success)
                    {
                        var timestamp = DateTime.ParseExact(match.Groups["timestamp"].Value, "dd.MM.yyyy HH:mm:ss",
                            CultureInfo.InvariantCulture);
                        var level = match.Groups["level"].Value;
                        var message = match.Groups["message"].Value;
                        return new LogEntry { Timestamp = timestamp, Level = level, Message = message };
                    }
                    else
                    {
                        return new LogEntry { Message = line };
                    }
                });

            var formattedLogs = logEntries.Select(log => formatter.Format(log).Message);

            string logs = string.Join(Environment.NewLine, formattedLogs);

            string filePath = LogPathHelper.GetLogFilePath(SelectedFormat);

            File.WriteAllText(filePath, logs);
            TextBlock = "Logs saved successfully.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
            TextBlock = $"An error occurred: {ex.Message}";
        }
    }

    private void OpenFolder()
    {
        try
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LogAnalyzerForWindows");
            _fileSystemService.OpenFolder(defaultPath, UpdateTextBlock);

            TextBlock = "Folder opened.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
            TextBlock = $"An error occurred: {ex.Message}";
        }
    }

    private void ArchiveLatestFolder()
    {
        try
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LogAnalyzerForWindows");

            _fileSystemService.ArchiveLatestFolder(defaultPath, UpdateTextBlock);
            TextBlock = "Latest folder archived.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
            TextBlock = $"An error occurred: {ex.Message}";
        }
    }

    private async Task SendEmailAsync()
    {
        try
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LogAnalyzerForWindows");
            var zipFiles = Directory.GetFiles(defaultPath, "*.zip");
            var latestZipFile = zipFiles.OrderByDescending(f => File.GetCreationTime(f)).First();

            await _emailService.SendEmailAsync("Recipient Name", UserEmail, "Log Analyzer For Windows",
                "Here is the latest log file.", latestZipFile);
            TextBlock = "Email sent.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
            TextBlock = $"An error occurred: {ex.Message}";
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        OnPropertyChanged(nameof(IsFolderExists));
    }

    private void OnMonitoringStartedOrStopped()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
        });
    }

    private void UpdateTextBlock(string message)
    {
        TextBlock = message;
    }

    private void UpdateOutputText(string text)
    {
        OutputText += text + Environment.NewLine;
        IsLoading = false;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}