namespace LogAnalyzerForWindows.Database;

internal static class DbContextConfig
{
    private static readonly string DbFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AzioEventLogAnalyzer");

    public static string DbPath
    {
        get
        {
            if (!Directory.Exists(DbFolder))
            {
                Directory.CreateDirectory(DbFolder);
            }
            return Path.Combine(DbFolder, "logs.db");
        }
    }

    public static string ConnectionString => $"Data Source={DbPath}";
}
