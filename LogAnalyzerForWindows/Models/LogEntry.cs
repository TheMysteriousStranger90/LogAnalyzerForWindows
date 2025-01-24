using System;

namespace LogAnalyzerForWindows.Models;

public class LogEntry
{
    public DateTime? Timestamp { get; set; }
    public string Level { get; set; }
    public string Message { get; set; }

    public override bool Equals(object obj)
    {
        if (obj == null || GetType() != obj.GetType())
            return false;

        LogEntry other = (LogEntry)obj;
        return Timestamp?.ToString() == other.Timestamp?.ToString() &&
               Level == other.Level &&
               Message == other.Message;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (Timestamp?.ToString().GetHashCode() ?? 0);
            hash = hash * 23 + (Level?.GetHashCode() ?? 0);
            hash = hash * 23 + (Message?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public override string ToString()
    {
        return $"{Timestamp} {Level} {Message}";
    }
}