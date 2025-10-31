using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ILogMonitor
{
    event EventHandler<LogsChangedEventArgs> LogsChanged;
    void Monitor(ILogReader reader);
}
