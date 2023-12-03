using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Analyzer;
using LogAnalyzerForWindows.Models.Filter;
using LogAnalyzerForWindows.Models.Formatter;
using LogAnalyzerForWindows.Models.Formatter.Interfaces;
using LogAnalyzerForWindows.Models.Reader;
using LogAnalyzerForWindows.Models.Reader.Interfaces;
using LogAnalyzerForWindows.Models.Writer;
using LogAnalyzerForWindows.Models.Writer.Interfaces;

namespace LogAnalyzerForWindows.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    private string _selectedLogLevel;
    private string _selectedTime;
    private ICommand _startCommand;
    private ICommand _stopCommand;
    public ICommand SaveCommand => new RelayCommand(Save);

    //public AvaloniaList<string> LogLevels { get; } = new AvaloniaList<string> { "Трассировка", "Отладка", "Информация", "Предупреждение", "Ошибка", "Критический" };
    public AvaloniaList<string> LogLevels { get; } = new AvaloniaList<string>
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    public AvaloniaList<string> Times { get; } = new AvaloniaList<string> { "Last hour", "Last 24 hours", "All time" };

    public AvaloniaList<string> Formats { get; } = new AvaloniaList<string> { "txt", "json", "xml" };

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
    
    public string SelectedFormat { get; set; }

    private LogMonitor _monitor;

    public MainWindowViewModel()
    {
        _monitor = new LogMonitor();

        _monitor.MonitoringStarted += OnMonitoringStartedOrStopped;
        _monitor.MonitoringStopped += OnMonitoringStartedOrStopped;

        StartCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
        StopCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);
    }

    private bool CanStartMonitoring()
    {
        return SelectedLogLevel != null && SelectedTime != null;
    }

    private bool CanStopMonitoring()
    {
        return _monitor.IsMonitoring;
    }

    private async void StartMonitoring()
    {
        Debug.WriteLine("StartMonitoring called.");
        IsLoading = true;
        await Task.Run(() =>
        {
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
                case "All time":
                    timeSpan = TimeSpan.MaxValue;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown time interval: {SelectedTime}");
            }

            TimeFilter filter = new TimeFilter(timeSpan);

            _monitor.LogsChanged -= OnLogsChanged;
            _monitor.LogsChanged += OnLogsChanged;

            void OnLogsChanged(IEnumerable<LogEntry> logs)
            {
                var filteredLogs = filter.Filter(logs);
                manager.ProcessLogs(filteredLogs);
            }

            _monitor.Monitor(reader);
        });

        IsLoading = false;

        ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
    }

    private void StopMonitoring()
    {
        _monitor.StopMonitoring();

        ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
    }

    private void Save()
    {
        ILogFormatter formatter;
        switch (SelectedFormat)
        {
            case "txt":
                formatter = new LogFormatter();
                break;
            case "json":
                formatter = new JsonLogFormatter();
                break;
            case "xml":
                formatter = new XmlLogFormatter();
                break;
            default:
                throw new InvalidOperationException($"Unknown format: {SelectedFormat}");
        }

        var logEntries = OutputText.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new LogEntry { Message = line });

        var formattedLogs = logEntries.Select(log => formatter.Format(log).Message);

        string logs = string.Join(Environment.NewLine, formattedLogs);

        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LogAnalyzerForWindows");
        Directory.CreateDirectory(defaultPath);

        string deviceFolderPath = Path.Combine(defaultPath, $"{DateTime.Now:yyyyMMdd}");
        Directory.CreateDirectory(deviceFolderPath);

        string fileName = $"output_{DateTime.Now:yyyyMMdd_HHmmss}.{SelectedFormat}";
        string filePath = Path.Combine(deviceFolderPath, fileName);

        File.WriteAllText(filePath, logs);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnMonitoringStartedOrStopped()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
        });
    }

    private void UpdateOutputText(string text)
    {
        OutputText += text + Environment.NewLine;
        IsLoading = false;
    }
}