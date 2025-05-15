using System;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Formatter;

public class LogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        if (log == null) throw new ArgumentNullException(nameof(log));

        return log;
    }
}