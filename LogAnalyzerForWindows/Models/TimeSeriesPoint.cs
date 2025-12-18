namespace LogAnalyzerForWindows.Models;

internal sealed class TimeSeriesPoint
{
    public DateTime Time { get; init; }
    public int Count { get; init; }
    public string? Label { get; init; }
}
