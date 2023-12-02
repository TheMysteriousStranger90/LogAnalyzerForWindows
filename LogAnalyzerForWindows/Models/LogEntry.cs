using System;

namespace LogAnalyzerForWindows.Models;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; }
    public string Message { get; set; }

    public override string ToString()
    {
        return $"{Timestamp} {Level} {Message}";
    }
}