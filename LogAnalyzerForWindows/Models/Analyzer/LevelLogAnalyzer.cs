using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzerForWindows.Models.Analyzer;

public class LevelLogAnalyzer : LogAnalyzer
{
    private string _level;

    public LevelLogAnalyzer(string level)
    {
        _level = level;
    }

    public override void Analyze(IEnumerable<LogEntry> logs)
    {
        int count = 0;

        foreach (var log in logs)
        {
            if (log.Level?.ToLower() == _level?.ToLower())
            {
                count++;
            }
        }

        Console.WriteLine($"Number of {_level} logs: {count}");
    }

    public IEnumerable<LogEntry> FilterByLevel(IEnumerable<LogEntry> logs)
    {
        return logs.Where(log => log.Level?.ToLower() == _level?.ToLower());
    }
}