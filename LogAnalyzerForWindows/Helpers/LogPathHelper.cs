using System;
using System.IO;

namespace LogAnalyzerForWindows.Helpers;

public static class LogPathHelper
{
    public static string GetLogFilePath(string selectedFormat)
    {
        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LogAnalyzerForWindows");
        Directory.CreateDirectory(defaultPath);

        string deviceFolderPath = Path.Combine(defaultPath, $"{DateTime.Now:yyyyMMdd}");
        Directory.CreateDirectory(deviceFolderPath);

        string fileName = $"output_{DateTime.Now:yyyyMMdd_HHmmss}.{selectedFormat}";
        string filePath = Path.Combine(deviceFolderPath, fileName);

        return filePath;
    }
}