namespace LogAnalyzerForWindows;

public class AllLevelsLogAnalyzer : LogAnalyzer
{
    public override void Analyze(IEnumerable<LogEntry> logs)
    {
        var levelCounts = new Dictionary<string, int>();

        foreach (var log in logs)
        {
            if (!levelCounts.ContainsKey(log.Level))
            {
                levelCounts[log.Level] = 0;
            }

            levelCounts[log.Level]++;
        }

        foreach (var levelCount in levelCounts)
        {
            Console.WriteLine($"Number of {levelCount.Key} logs: {levelCount.Value}");
        }
    }
}