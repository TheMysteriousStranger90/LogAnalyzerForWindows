using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Formatter;

internal sealed class LogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        ArgumentNullException.ThrowIfNull(log);

        return log;
    }
}
