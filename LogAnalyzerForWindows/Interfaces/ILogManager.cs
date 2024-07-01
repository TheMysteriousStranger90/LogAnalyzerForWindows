using System.Collections.Generic;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

public interface ILogManager
{
    void ProcessLogs(IEnumerable<LogEntry> logs);
}