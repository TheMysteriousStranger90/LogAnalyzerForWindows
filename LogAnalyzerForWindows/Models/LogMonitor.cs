using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

public class LogMonitor
{
    public event Action<IEnumerable<LogEntry>> LogsChanged;
    public event Action MonitoringStarted;
    public event Action MonitoringStopped;

    private bool _shouldStop = false;
    private bool _isMonitoring;

    public bool IsMonitoring
    {
        get { return _isMonitoring; }
    }

    public void Monitor(ILogReader reader)
    {
        IEnumerable<LogEntry> previousLogs = null;
        _isMonitoring = true;
        MonitoringStarted?.Invoke();

        while (!_shouldStop)
        {
            IEnumerable<LogEntry> currentLogs = reader.ReadLogs();

            if (previousLogs == null || !currentLogs.SequenceEqual(previousLogs))
            {
                LogsChanged?.Invoke(currentLogs);
                previousLogs = currentLogs;
            }

            Thread.Sleep(1000);
        }
    }

    public void StopMonitoring()
    {
        _shouldStop = true;
        _isMonitoring = false;
        MonitoringStopped?.Invoke();
    }
}