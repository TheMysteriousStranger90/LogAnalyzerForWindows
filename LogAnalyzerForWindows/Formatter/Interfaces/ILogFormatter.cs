namespace LogAnalyzerForWindows.Models.Formatter.Interfaces;

public interface ILogFormatter
{
    LogEntry Format(LogEntry log);
}