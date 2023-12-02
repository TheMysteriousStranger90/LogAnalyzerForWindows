using LogAnalyzerForWindows.Models.Formatter.Interfaces;

namespace LogAnalyzerForWindows.Models.Formatter;

public class LogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        log.Message = $"{log.Timestamp} {log.Level} {log.Message}";
        return log;
    }
}