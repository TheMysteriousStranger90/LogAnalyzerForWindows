using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Database.Repositories;

internal interface ILogRepository
{
    Task<int> SaveLogsAsync(IEnumerable<LogEntry> logs, string sessionId);
    Task<(List<LogEntry> Logs, int TotalCount)> GetLogsAsync(
        int pageNumber,
        int pageSize,
        string? levelFilter = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? sessionId = null);
    Task<List<string>> GetSessionIdsAsync();
    Task<int> DeleteOldLogsAsync(DateTime olderThan);
    Task<int> ClearAllLogsAsync();
    Task<Dictionary<string, int>> GetLogStatisticsAsync(string? sessionId = null);
}
