using System.Text.Json;
using LogAnalyzerForWindows.Converters;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Formatter;

public class JsonLogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new DateTimeConverter() }
        };
        
        var json = JsonSerializer.Serialize(log, options);
        
        return new LogEntry { Message = json };
    }
}