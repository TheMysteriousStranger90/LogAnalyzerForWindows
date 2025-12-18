namespace LogAnalyzerForWindows.Models;

internal sealed class LogStatistics
{
    public int TotalLogs { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InformationCount { get; init; }
    public int AuditSuccessCount { get; init; }
    public int AuditFailureCount { get; init; }
    public int OtherCount { get; init; }

    public Dictionary<string, int> LogsBySource { get; init; } = new();
    public Dictionary<DateTime, int> LogsByHour { get; init; } = new();
    public Dictionary<DateTime, int> LogsByDay { get; init; } = new();
    public Dictionary<int, int> TopEventIds { get; init; } = new();
}
