using System;
using LogAnalyzerForWindows.Models.Formatter.Interfaces;
using LogAnalyzerForWindows.Models.Writer.Interfaces;

namespace LogAnalyzerForWindows.Models.Writer;

public class ConsoleLogWriter : ILogWriter
{
    private ILogFormatter _formatter;

    public ConsoleLogWriter(ILogFormatter formatter)
    {
        _formatter = formatter;
    }

    public void Write(LogEntry log)
    {
        var message = _formatter.Format(log);
        Console.WriteLine(message);
    }
}