using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzerForWindows.Filter.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Filter;

public class TimeFilter
{
    private TimeSpan _timeSpan;
    private ITimeProvider _timeProvider;

    public TimeFilter(TimeSpan timeSpan, ITimeProvider timeProvider = null)
    {
        _timeSpan = timeSpan;
        _timeProvider = timeProvider ?? new SystemTimeProvider();
    }

    public IEnumerable<LogEntry> Filter(IEnumerable<LogEntry> logs)
    {
        if (logs == null) throw new ArgumentNullException(nameof(logs));

        var cutoff = _timeProvider.GetCurrentTime() - _timeSpan;
        return logs.Where(log => log.Timestamp >= cutoff);
    }
}