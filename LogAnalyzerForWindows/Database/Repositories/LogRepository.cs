using LogAnalyzerForWindows.Models;
using Microsoft.EntityFrameworkCore;

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
        string? sessionId = null)
    {
        var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (context)
        {
            var query = context.LogEntries.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(levelFilter))
            {
                query = query.Where(e => e.Level == levelFilter);
            }

            if (startDate.HasValue)
            {
                query = query.Where(e => e.Timestamp >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(e => e.Timestamp <= endDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                query = query.Where(e => e.SessionId == sessionId);
            }

            var totalCount = await query.CountAsync().ConfigureAwait(false);

            var logs = await query
                .OrderByDescending(e => e.Timestamp)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new LogEntry
                {
                    Timestamp = e.Timestamp,
                    Level = e.Level,
                    Message = e.Message
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
            return await context.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries").ConfigureAwait(false);
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
}
