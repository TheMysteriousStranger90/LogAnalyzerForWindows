namespace LogAnalyzerForWindows.Models;

internal sealed class LogEntry
{
    public DateTime? Timestamp { get; set; }
    public string? Level { get; set; }
    public string? Message { get; set; }
    public int? EventId { get; set; }
    public string? Source { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;

        LogEntry other = (LogEntry)obj;
        return Nullable.Equals(Timestamp, other.Timestamp) &&
               Level == other.Level &&
               Message == other.Message &&
               EventId == other.EventId &&
               Source == other.Source;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (Timestamp?.GetHashCode() ?? 0);
            hash = hash * 23 + (Level?.GetHashCode(StringComparison.Ordinal) ?? 0);
            hash = hash * 23 + (Message?.GetHashCode(StringComparison.Ordinal) ?? 0);
            hash = hash * 23 + (EventId?.GetHashCode() ?? 0);
            hash = hash * 23 + (Source?.GetHashCode(StringComparison.Ordinal) ?? 0);
            return hash;
        }
    }

    public override string ToString()
    {
        string timestampStr = Timestamp?.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A";
        string levelStr = Level ?? "N/A";
        string eventIdStr = EventId.HasValue ? $"[{EventId}]" : "";
        string sourceStr = !string.IsNullOrEmpty(Source) ? $"({Source})" : "";
        return $"{timestampStr} {levelStr} {eventIdStr} {sourceStr} {Message}";
    }
}
