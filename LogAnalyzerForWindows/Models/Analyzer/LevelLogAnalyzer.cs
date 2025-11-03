namespace LogAnalyzerForWindows.Models.Analyzer;

internal sealed class LevelLogAnalyzer : LogAnalyzer
{
    private readonly string _levelToAnalyze;

    public LevelLogAnalyzer(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            throw new ArgumentException("Level cannot be null or whitespace.", nameof(level));
        }

        _levelToAnalyze = level.ToUpperInvariant();
    }

    public override void Analyze(IEnumerable<LogEntry> logs)
    {
        if (logs == null)
        {
            Console.WriteLine($"Number of {_levelToAnalyze} logs: 0");
            return;
        }

        int count = logs.Count(log => log?.Level?.ToUpperInvariant() == _levelToAnalyze);

        Console.WriteLine($"Number of {_levelToAnalyze} logs: {count}");
    }

    public IEnumerable<LogEntry> FilterByLevel(IEnumerable<LogEntry> logs)
    {
        if (logs == null)
        {
            return Enumerable.Empty<LogEntry>();
        }

        return logs.Where(log => log?.Level?.ToUpperInvariant() == _levelToAnalyze);
    }
}
