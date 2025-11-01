using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ILogManager
{
    Task ProcessLogsAsync(IEnumerable<LogEntry> logs, CancellationToken cancellationToken = default);
}
