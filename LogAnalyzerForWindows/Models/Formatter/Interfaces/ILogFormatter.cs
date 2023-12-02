namespace LogAnalyzerForWindows.Formatter.Interfaces;

public interface ILogFormatter
{
    LogEntry Format(LogEntry log);
}