using System;
using LogAnalyzerForWindows.Filter.Interfaces;

namespace LogAnalyzerForWindows.Filter;

public class SystemTimeProvider : ITimeProvider
{
    public DateTime GetCurrentTime() => DateTime.UtcNow;
}
