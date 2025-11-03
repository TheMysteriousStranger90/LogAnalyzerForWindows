namespace LogAnalyzerForWindows.Models.Reader.Interfaces;

internal interface ILogReader
{
    IEnumerable<LogEntry> ReadLogs();
}
