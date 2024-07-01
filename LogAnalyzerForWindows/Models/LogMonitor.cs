using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

public class LogMonitor : ILogMonitor
{
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
        IEnumerable<LogEntry> previousLogs = null;
        _isMonitoring = true;
        MonitoringStarted?.Invoke();

        while (!_cts.Token.IsCancellationRequested)
        {
            IEnumerable<LogEntry> currentLogs = reader.ReadLogs();

            if (previousLogs == null || !currentLogs.SequenceEqual(previousLogs))
            {
                LogsChanged?.Invoke(currentLogs);
                previousLogs = currentLogs;
            }

            Thread.Sleep(1000);
        }

        if (!_cts.Token.IsCancellationRequested)
        {
            _isMonitoring = false;
            MonitoringStopped?.Invoke();
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