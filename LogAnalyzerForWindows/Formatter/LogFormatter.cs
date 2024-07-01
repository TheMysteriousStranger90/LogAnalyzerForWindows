using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Formatter;

public class LogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        log.Message = $"{log.Message}";
        return log;
    }
}