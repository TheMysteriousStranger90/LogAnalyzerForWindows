using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ILogStatisticsService
{
    Task<LogStatistics> GetStatisticsAsync(string? sessionId, CancellationToken ct = default);
    Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(string? sessionId, TimeSpan? groupBy, CancellationToken ct = default);
    Task<List<(string Source, int Count)>> GetTopSourcesAsync(int top, string? sessionId, CancellationToken ct = default);
    Task<List<(int EventId, int Count)>> GetTopEventIdsAsync(int top, string? sessionId, CancellationToken ct = default);
    Task<List<string>> GetSessionsAsync(CancellationToken ct = default);
    void InvalidateCache(string? sessionId = null);
}
