using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzerForWindows.Models.Analyzer;

public class LevelLogAnalyzer : LogAnalyzer
{
    private readonly string _levelToAnalyze;

    public LevelLogAnalyzer(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            throw new ArgumentException("Level cannot be null or whitespace.", nameof(level));
        }

        _levelToAnalyze = level.ToLowerInvariant();
    }

    public override void Analyze(IEnumerable<LogEntry> logs)
    {
        if (logs == null)
        {
            Console.WriteLine($"Number of {_levelToAnalyze} logs: 0");
            return;
        }

        int count = logs.Count(log => log?.Level?.ToLowerInvariant() == _levelToAnalyze);

        Console.WriteLine($"Number of {_levelToAnalyze} logs: {count}");
    }

    public IEnumerable<LogEntry> FilterByLevel(IEnumerable<LogEntry> logs)
    {
        if (logs == null)
        {
            return Enumerable.Empty<LogEntry>();
        }

        return logs.Where(log => log?.Level?.ToLowerInvariant() == _levelToAnalyze);
    }
}