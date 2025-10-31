using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Formatter.Interfaces;

internal interface ILogFormatter
{
    LogEntry Format(LogEntry log);
}
