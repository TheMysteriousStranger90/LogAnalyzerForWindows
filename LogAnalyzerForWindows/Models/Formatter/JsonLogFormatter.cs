using System.Text.Json;
using LogAnalyzerForWindows.Formatter.Interfaces;

namespace LogAnalyzerForWindows.Formatter;

public class JsonLogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        var json = JsonSerializer.Serialize(log);
        return new LogEntry { Message = json };
    }
}