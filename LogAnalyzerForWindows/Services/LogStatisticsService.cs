using System.Collections.Concurrent;
using LogAnalyzerForWindows.Database.Repositories;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Services;

internal sealed class LogStatisticsService : ILogStatisticsService
{
    private readonly ILogRepository _repository;
    private readonly ConcurrentDictionary<string, CacheEntry<LogStatistics>> _statsCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<TimeSeriesPoint>>> _timeSeriesCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<(string, int)>>> _sourcesCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<(int, int)>>> _eventIdsCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<string>>> _sessionsCache = new();

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public LogStatisticsService(ILogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<LogStatistics> GetStatisticsAsync(string? sessionId, CancellationToken ct = default)
    {
        var cacheKey = $"stats_{sessionId ?? "all"}";

        if (TryGetFromCache(_statsCache, cacheKey, out var cached))
            return cached!;

        var stats = await _repository.GetDetailedStatisticsAsync(sessionId, cancellationToken: ct)
            .ConfigureAwait(false);

        AddToCache(_statsCache, cacheKey, stats);

        return stats;
    }

    public async Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(string? sessionId, TimeSpan? groupBy,
        CancellationToken ct = default)
    {
        var cacheKey = $"timeseries_{sessionId ?? "all"}_{groupBy?.TotalMinutes ?? 60}";

        if (TryGetFromCache(_timeSeriesCache, cacheKey, out var cached))
            return cached!;

        var timeSeries = await _repository.GetLogsTimeSeriesAsync(sessionId, groupBy: groupBy, cancellationToken: ct)
            .ConfigureAwait(false);

        AddToCache(_timeSeriesCache, cacheKey, timeSeries);

        return timeSeries;
    }

    public async Task<List<(string Source, int Count)>> GetTopSourcesAsync(int top, string? sessionId,
        CancellationToken ct = default)
    {
        var cacheKey = $"sources_{sessionId ?? "all"}_{top}";

        if (TryGetFromCache(_sourcesCache, cacheKey, out var cached))
            return cached!;

        var sources = await _repository.GetTopSourcesAsync(top, sessionId, ct).ConfigureAwait(false);

        AddToCache(_sourcesCache, cacheKey, sources);

        return sources;
    }

    public async Task<List<(int EventId, int Count)>> GetTopEventIdsAsync(int top, string? sessionId,
        CancellationToken ct = default)
    {
        var cacheKey = $"eventids_{sessionId ?? "all"}_{top}";

        if (TryGetFromCache(_eventIdsCache, cacheKey, out var cached))
            return cached!;

        var eventIds = await _repository.GetTopEventIdsAsync(top, sessionId, ct).ConfigureAwait(false);

        AddToCache(_eventIdsCache, cacheKey, eventIds);

        return eventIds;
    }

    public async Task<List<string>> GetSessionsAsync(CancellationToken ct = default)
    {
        const string cacheKey = "sessions";

        if (TryGetFromCache(_sessionsCache, cacheKey, out var cached))
            return cached!;

        var sessions = await _repository.GetSessionIdsAsync().ConfigureAwait(false);

        AddToCache(_sessionsCache, cacheKey, sessions);

        return sessions;
    }

    public void InvalidateCache(string? sessionId = null)
    {
        if (sessionId == null)
        {
            _statsCache.Clear();
            _timeSeriesCache.Clear();
            _sourcesCache.Clear();
            _eventIdsCache.Clear();
            _sessionsCache.Clear();
        }
        else
        {
            var keysToRemove = _statsCache.Keys
                .Where(k => k.Contains(sessionId, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keysToRemove)
                _statsCache.TryRemove(key, out _);

            keysToRemove = _timeSeriesCache.Keys
                .Where(k => k.Contains(sessionId, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keysToRemove)
                _timeSeriesCache.TryRemove(key, out _);

            keysToRemove = _sourcesCache.Keys
                .Where(k => k.Contains(sessionId, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keysToRemove)
                _sourcesCache.TryRemove(key, out _);

            keysToRemove = _eventIdsCache.Keys
                .Where(k => k.Contains(sessionId, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keysToRemove)
                _eventIdsCache.TryRemove(key, out _);

            _sessionsCache.Clear();
        }
    }

    private static bool TryGetFromCache<T>(ConcurrentDictionary<string, CacheEntry<T>> cache, string key, out T? value)
    {
        if (cache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static void AddToCache<T>(ConcurrentDictionary<string, CacheEntry<T>> cache, string key, T value)
    {
        cache[key] = new CacheEntry<T>(value, DateTime.UtcNow.Add(CacheDuration));
    }

    private sealed class CacheEntry<T>
    {
        public T Value { get; }
        public DateTime ExpiresAt { get; }
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

        public CacheEntry(T value, DateTime expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }
    }
}
