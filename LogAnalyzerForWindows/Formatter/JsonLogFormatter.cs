using System.Text.Json;
using LogAnalyzerForWindows.Converters;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Formatter;

internal sealed class JsonLogFormatter : ILogFormatter
{
    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new DateTimeConverter() }
    };

    public LogEntry Format(LogEntry log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var json = JsonSerializer.Serialize(log, _options);

        return new LogEntry { Message = json, Timestamp = log.Timestamp, Level = log.Level };
    }
}
