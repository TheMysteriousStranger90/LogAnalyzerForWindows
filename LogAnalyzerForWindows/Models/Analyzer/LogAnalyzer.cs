namespace LogAnalyzerForWindows.Models.Analyzer;

internal abstract class LogAnalyzer
{
    public abstract void Analyze(IEnumerable<LogEntry> logs);
}
