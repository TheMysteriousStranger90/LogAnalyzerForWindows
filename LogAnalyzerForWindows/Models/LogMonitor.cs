using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

public class LogMonitor
{
    public event Action<IEnumerable<LogEntry>> LogsChanged;

    private bool _shouldStop = false;

    public void Monitor(ILogReader reader)
    {
        IEnumerable<LogEntry> previousLogs = null;

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
    }
}