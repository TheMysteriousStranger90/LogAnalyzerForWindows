using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

public class LogMonitor : ILogMonitor
{
    private HashSet<LogEntry> _lastProcessedLogs = new HashSet<LogEntry>();
    public event Action<IEnumerable<LogEntry>> LogsChanged;
    public event Action MonitoringStarted;
    public event Action MonitoringStopped;
    private CancellationTokenSource _cts;
    private bool _isMonitoring;

    public bool IsMonitoring
    {
        get { return _isMonitoring; }
    }


    public void Monitor(ILogReader reader)
    {
        _cts = new CancellationTokenSource();
        _isMonitoring = true;
        MonitoringStarted?.Invoke();

        while (!_cts.Token.IsCancellationRequested)
        {
            var currentLogs = reader.ReadLogs().ToList();
            var newLogs = currentLogs.Except(_lastProcessedLogs).ToList();

            if (newLogs.Any())
            {
                LogsChanged?.Invoke(newLogs);
                _lastProcessedLogs = new HashSet<LogEntry>(currentLogs);
            }

            Thread.Sleep(1000);
        }
    }

    public void StopMonitoring()
    {
        if (_cts != null)
        {
            _cts.Cancel();
        }
    }
}