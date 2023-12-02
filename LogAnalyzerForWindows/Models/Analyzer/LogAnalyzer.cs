using System.Collections.Generic;

namespace LogAnalyzerForWindows.Models.Analyzer;

public abstract class LogAnalyzer
{
    public abstract void Analyze(IEnumerable<LogEntry> logs);
}