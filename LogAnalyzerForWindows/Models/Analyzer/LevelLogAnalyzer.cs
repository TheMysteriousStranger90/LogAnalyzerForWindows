namespace LogAnalyzerForWindows;

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
            if (log.Level == _level)
            {
                count++;
            }
        }

        Console.WriteLine($"Number of {_level} logs: {count}");
    }
}