using LogAnalyzerForWindows.Filter.Interfaces;

namespace LogAnalyzerForWindows.Filter;

internal sealed class SystemTimeProvider : ITimeProvider
{
    public DateTime GetCurrentTime()
    {
        return DateTime.Now;
    }
}
