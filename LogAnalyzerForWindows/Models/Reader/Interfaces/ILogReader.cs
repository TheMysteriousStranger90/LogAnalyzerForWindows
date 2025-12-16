namespace LogAnalyzerForWindows.Models.Reader.Interfaces;

internal interface ILogReader
{
    IEnumerable<LogEntry> ReadLogs();
    Task<List<LogEntry>> ReadLogsAsync(CancellationToken cancellationToken = default);
}
