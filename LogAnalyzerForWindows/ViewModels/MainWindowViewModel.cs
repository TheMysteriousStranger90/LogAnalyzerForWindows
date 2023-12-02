using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Collections;
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
    
    public AvaloniaList<string> LogLevels { get; } = new AvaloniaList<string> { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    public AvaloniaList<string> Times { get; } = new AvaloniaList<string> { "Last hour", "Last 24 hours", "All time" };

    public string SelectedLogLevel
    {
        get { return _selectedLogLevel; }
        set
        {
            if (value != _selectedLogLevel)
            {
                _selectedLogLevel = value;
                OnPropertyChanged();
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
            }
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

    private LogMonitor _monitor;

    public MainWindowViewModel()
    {
        _monitor = new LogMonitor();

        StartCommand = new RelayCommand(StartMonitoring);
        StopCommand = new RelayCommand(StopMonitoring);
    }

    private void StartMonitoring()
    {
        ILogReader reader = new WindowsEventLogReader("System");
        LogAnalyzer analyzer = new LevelLogAnalyzer(SelectedLogLevel);
        ILogFormatter formatter = new LogFormatter();
        ILogWriter writer = new ConsoleLogWriter(formatter);

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

        _monitor.LogsChanged += logs =>
        {
            var filteredLogs = filter.Filter(logs);
            manager.ProcessLogs(filteredLogs);
        };

        _monitor.Monitor(reader);
    }

    private void StopMonitoring()
    {
        _monitor.StopMonitoring();
    }
    
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}