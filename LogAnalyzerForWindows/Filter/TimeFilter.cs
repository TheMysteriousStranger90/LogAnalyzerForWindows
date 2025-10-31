using LogAnalyzerForWindows.Filter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Filter;

internal sealed class TimeFilter
{
    private readonly TimeSpan _timeSpan;
    private readonly ITimeProvider _timeProvider;

    public TimeFilter(TimeSpan timeSpan, ITimeProvider? timeProvider = null)
    {
        _timeSpan = timeSpan;
        _timeProvider = timeProvider ?? new SystemTimeProvider();
    }

    public IEnumerable<LogEntry> Filter(IEnumerable<LogEntry> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        var cutoff = _timeProvider.GetCurrentTime() - _timeSpan;
        return logs.Where(log => log.Timestamp.HasValue && log.Timestamp.Value >= cutoff);
    }
}
