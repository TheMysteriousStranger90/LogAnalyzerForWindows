using System.Text.Json;
using LogAnalyzerForWindows.Models.Formatter.Interfaces;

namespace LogAnalyzerForWindows.Models.Formatter;

public class JsonLogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        var json = JsonSerializer.Serialize(log);
        return new LogEntry { Message = json };
    }
}