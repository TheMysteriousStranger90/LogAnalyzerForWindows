namespace LogAnalyzerForWindows.Helpers;

internal static class LogPathHelper
{
    public static string GetLogFilePath(string selectedFormat)
    {
        if (string.IsNullOrWhiteSpace(selectedFormat))
        {
            throw new ArgumentException("Selected format cannot be null or whitespace.", nameof(selectedFormat));
        }

        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string appDirectoryName = "LogAnalyzerForWindows";

        string defaultPath = Path.Combine(baseDirectory, appDirectoryName);
        Directory.CreateDirectory(defaultPath);

        string deviceFolderPath = Path.Combine(defaultPath, $"{DateTime.UtcNow:yyyyMMdd}");
        Directory.CreateDirectory(deviceFolderPath);

        string fileName = $"output_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{selectedFormat}";
        string filePath = Path.Combine(deviceFolderPath, fileName);

        return filePath;
    }
}
