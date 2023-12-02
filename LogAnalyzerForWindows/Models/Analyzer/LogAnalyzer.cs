namespace LogAnalyzerForWindows;

public abstract class LogAnalyzer
{
    public abstract void Analyze(IEnumerable<LogEntry> logs);
}