﻿namespace LogAnalyzerForWindows.Filter;

public class TimeFilter
{
    private TimeSpan _timeSpan;

    public TimeFilter(TimeSpan timeSpan)
    {
        _timeSpan = timeSpan;
    }

    public IEnumerable<LogEntry> Filter(IEnumerable<LogEntry> logs)
    {
        var cutoff = DateTime.Now - _timeSpan;
        return logs.Where(log => log.Timestamp >= cutoff);
    }
}