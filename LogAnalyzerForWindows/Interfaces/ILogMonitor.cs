using System;
using System.Collections.Generic;
using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Interfaces;

public interface ILogMonitor
{
    event Action<IEnumerable<LogEntry>> LogsChanged;
    void Monitor(ILogReader reader);
}