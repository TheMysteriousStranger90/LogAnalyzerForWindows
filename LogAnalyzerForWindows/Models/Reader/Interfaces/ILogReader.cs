namespace LogAnalyzerForWindows.Models.Reader.Interfaces;

internal interface ILogReader
{
    Task<List<LogEntry>> ReadLogsAsync(CancellationToken cancellationToken = default);
}
