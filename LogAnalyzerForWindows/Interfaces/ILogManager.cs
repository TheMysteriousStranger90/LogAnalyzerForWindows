using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ILogManager
{
    void ProcessLogs(IEnumerable<LogEntry> logs);
}
