using LogAnalyzerForWindows.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LogAnalyzerForWindows.Database.Repositories;

internal sealed class LogRepository : ILogRepository
{
    private readonly IDbContextFactory<LogAnalyzerDbContext> _contextFactory;

    public LogRepository(IDbContextFactory<LogAnalyzerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int> SaveLogsAsync(IEnumerable<LogEntry> logs, string sessionId)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            var entities = logs.Select(log => new LogEntryEntity
            {
                Timestamp = log.Timestamp,
                Level = log.Level,
                Message = log.Message,
                EventId = log.EventId,
                Source = log.Source,
                CreatedAt = DateTime.UtcNow,
                SessionId = sessionId
            }).ToList();

            await context.LogEntries.AddRangeAsync(entities).ConfigureAwait(false);
            return await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    public async Task<(List<LogEntry> Logs, int TotalCount)> GetLogsAsync(
        int pageNumber,
        int pageSize,
        string? levelFilter = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? sessionId = null,
        string? searchText = null,
        int? eventIdFilter = null,
        string? sourceFilter = null)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            var query = context.LogEntries.AsNoTracking();

            query = ApplyFilters(query, levelFilter, startDate, endDate, sessionId, searchText, eventIdFilter,
                sourceFilter);

            var totalCount = await query.CountAsync().ConfigureAwait(false);

            var logs = await query
                .OrderByDescending(e => e.Timestamp)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new LogEntry
                {
                    Timestamp = e.Timestamp,
                    Level = e.Level,
                    Message = e.Message,
                    EventId = e.EventId,
                    Source = e.Source
                })
                .ToListAsync().ConfigureAwait(false);

            return (logs, totalCount);
        }
    }

    public async Task<List<string>> GetSessionIdsAsync()
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            return await context.LogEntries
                .AsNoTracking()
                .Where(e => e.SessionId != null)
                .Select(e => e.SessionId!)
                .Distinct()
                .OrderByDescending(s => s)
                .ToListAsync().ConfigureAwait(false);
        }
    }

    public async Task<List<string>> GetDistinctSourcesAsync(string? sessionId = null)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            var query = context.LogEntries.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                query = query.Where(e => e.SessionId == sessionId);
            }

            return await query
                .Where(e => e.Source != null)
                .Select(e => e.Source!)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync().ConfigureAwait(false);
        }
    }

    public async Task<List<int>> GetDistinctEventIdsAsync(string? sessionId = null, int maxCount = 100)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            var query = context.LogEntries.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                query = query.Where(e => e.SessionId == sessionId);
            }

            return await query
                .Where(e => e.EventId != null)
                .Select(e => e.EventId!.Value)
                .Distinct()
                .OrderBy(id => id)
                .Take(maxCount)
                .ToListAsync().ConfigureAwait(false);
        }
    }

    public async Task<int> DeleteOldLogsAsync(DateTime olderThan)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            return await context.LogEntries
                .Where(e => e.CreatedAt < olderThan)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);
        }
    }

    public async Task<int> ClearAllLogsAsync()
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            return await context.LogEntries
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);
        }
    }

    public async Task<Dictionary<string, int>> GetLogStatisticsAsync(string? sessionId = null)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            var query = context.LogEntries.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                query = query.Where(e => e.SessionId == sessionId);
            }

            return await query
                .GroupBy(e => e.Level ?? "Unknown")
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Level, x => x.Count).ConfigureAwait(false);
        }
    }

    public async Task<LogStatistics> GetDetailedStatisticsAsync(
        string? sessionId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var query = context.LogEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(sessionId))
                query = query.Where(l => l.SessionId == sessionId);

            if (startDate.HasValue)
                query = query.Where(l => l.Timestamp >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(l => l.Timestamp <= endDate.Value);

            var levelCountsTask = query
                .GroupBy(l => l.Level ?? "Unknown")
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Level, x => x.Count, cancellationToken);

            var logsBySourceTask = query
                .Where(l => l.Source != null)
                .GroupBy(l => l.Source!)
                .Select(g => new { Source = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(15)
                .ToDictionaryAsync(x => x.Source, x => x.Count, cancellationToken);

            var topEventIdsTask = query
                .Where(l => l.EventId.HasValue)
                .GroupBy(l => l.EventId!.Value)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToDictionaryAsync(x => x.EventId, x => x.Count, cancellationToken);

            var logsByHourTask = query
                .Where(l => l.Timestamp.HasValue)
                .GroupBy(l => new
                    { l.Timestamp!.Value.Year, l.Timestamp.Value.Month, l.Timestamp.Value.Day, l.Timestamp.Value.Hour })
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ThenBy(x => x.Date.Hour)
                .ToListAsync(cancellationToken);

            var logsByDayTask = query
                .Where(l => l.Timestamp.HasValue)
                .GroupBy(l => new { l.Timestamp!.Value.Year, l.Timestamp.Value.Month, l.Timestamp.Value.Day })
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day)
                .ToListAsync(cancellationToken);

            await Task.WhenAll(levelCountsTask, logsBySourceTask, topEventIdsTask, logsByHourTask, logsByDayTask)
                .ConfigureAwait(false);

            var levelCounts = await levelCountsTask;
            var logsBySource = await logsBySourceTask;
            var topEventIds = await topEventIdsTask;
            var logsByHourRaw = await logsByHourTask;
            var logsByDayRaw = await logsByDayTask;

            var logsByHour = logsByHourRaw.ToDictionary(
                x => new DateTime(x.Date.Year, x.Date.Month, x.Date.Day, x.Date.Hour, 0, 0),
                x => x.Count);

            var logsByDay = logsByDayRaw.ToDictionary(
                x => new DateTime(x.Date.Year, x.Date.Month, x.Date.Day),
                x => x.Count);

            var totalCount = levelCounts.Values.Sum();

            return new LogStatistics
            {
                TotalLogs = totalCount,
                ErrorCount = levelCounts.GetValueOrDefault("Error", 0),
                WarningCount = levelCounts.GetValueOrDefault("Warning", 0),
                InformationCount = levelCounts.GetValueOrDefault("Information", 0),
                AuditSuccessCount = levelCounts.GetValueOrDefault("AuditSuccess", 0),
                AuditFailureCount = levelCounts.GetValueOrDefault("AuditFailure", 0),
                OtherCount = levelCounts.GetValueOrDefault("Other", 0) + levelCounts.GetValueOrDefault("Unknown", 0),
                LogsBySource = logsBySource,
                LogsByHour = logsByHour,
                LogsByDay = logsByDay,
                TopEventIds = topEventIds
            };
        }
    }

    public async Task<List<TimeSeriesPoint>> GetLogsTimeSeriesAsync(
        string? sessionId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        TimeSpan? groupBy = null,
        CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var query = context.LogEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(sessionId))
                query = query.Where(l => l.SessionId == sessionId);

            if (startDate.HasValue)
                query = query.Where(l => l.Timestamp >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(l => l.Timestamp <= endDate.Value);

            var interval = groupBy ?? TimeSpan.FromHours(1);

            List<TimeSeriesPoint> result;

            if (interval.TotalHours >= 24)
            {
                var grouped = await query
                    .Where(l => l.Timestamp.HasValue)
                    .GroupBy(l => new { l.Timestamp!.Value.Year, l.Timestamp.Value.Month, l.Timestamp.Value.Day })
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                result = grouped.Select(g => new TimeSeriesPoint
                {
                    Time = new DateTime(g.Date.Year, g.Date.Month, g.Date.Day),
                    Count = g.Count,
                    Label = new DateTime(g.Date.Year, g.Date.Month, g.Date.Day).ToString("dd MMM",
                        CultureInfo.InvariantCulture)
                }).ToList();
            }
            else
            {
                var grouped = await query
                    .Where(l => l.Timestamp.HasValue)
                    .GroupBy(l => new
                    {
                        l.Timestamp!.Value.Year, l.Timestamp.Value.Month, l.Timestamp.Value.Day, l.Timestamp.Value.Hour
                    })
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day)
                    .ThenBy(x => x.Date.Hour)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                result = grouped.Select(g => new TimeSeriesPoint
                {
                    Time = new DateTime(g.Date.Year, g.Date.Month, g.Date.Day, g.Date.Hour, 0, 0),
                    Count = g.Count,
                    Label = new DateTime(g.Date.Year, g.Date.Month, g.Date.Day, g.Date.Hour, 0, 0).ToString("HH:mm",
                        CultureInfo.InvariantCulture)
                }).ToList();
            }

            return result;
        }
    }

    public async Task<Dictionary<string, int>> GetLogsByLevelAsync(
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var query = context.LogEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(sessionId))
                query = query.Where(l => l.SessionId == sessionId);

            return await query
                .GroupBy(l => l.Level ?? "Unknown")
                .Select(g => new { Level = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Level, x => x.Count, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<List<(string Source, int Count)>> GetTopSourcesAsync(
        int top = 10,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var query = context.LogEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(sessionId))
                query = query.Where(l => l.SessionId == sessionId);

            var result = await query
                .Where(l => l.Source != null)
                .GroupBy(l => l.Source!)
                .Select(g => new { Source = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.Select(x => (x.Source, x.Count)).ToList();
        }
    }

    public async Task<List<(int EventId, int Count)>> GetTopEventIdsAsync(
        int top = 10,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var query = context.LogEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(sessionId))
                query = query.Where(l => l.SessionId == sessionId);

            var result = await query
                .Where(l => l.EventId.HasValue)
                .GroupBy(l => l.EventId!.Value)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.Select(x => (x.EventId, x.Count)).ToList();
        }
    }

    private static IQueryable<LogEntryEntity> ApplyFilters(
        IQueryable<LogEntryEntity> query,
        string? levelFilter,
        DateTime? startDate,
        DateTime? endDate,
        string? sessionId,
        string? searchText,
        int? eventIdFilter,
        string? sourceFilter)
    {
        if (!string.IsNullOrWhiteSpace(levelFilter))
            query = query.Where(e => e.Level == levelFilter);

        if (startDate.HasValue)
            query = query.Where(e => e.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(e => e.Timestamp <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(sessionId))
            query = query.Where(e => e.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var searchPattern = $"%{searchText}%";
            query = query.Where(e => e.Message != null && EF.Functions.Like(e.Message, searchPattern));
        }

        if (eventIdFilter.HasValue)
            query = query.Where(e => e.EventId == eventIdFilter.Value);

        if (!string.IsNullOrWhiteSpace(sourceFilter))
            query = query.Where(e => e.Source == sourceFilter);

        return query;
    }
}
