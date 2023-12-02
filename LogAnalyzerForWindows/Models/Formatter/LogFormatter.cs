using LogAnalyzerForWindows.Formatter.Interfaces;

namespace LogAnalyzerForWindows;

public class LogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        log.Message = $"{log.Timestamp} {log.Level} {log.Message}";
        return log;
    }
}