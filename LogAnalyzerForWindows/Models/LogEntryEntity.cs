namespace LogAnalyzerForWindows.Models;

internal sealed class LogEntryEntity
{
    public int Id { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? Level { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? SessionId { get; set; }
}
