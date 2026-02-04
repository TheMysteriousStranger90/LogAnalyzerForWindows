using LogAnalyzerForWindows.Formatter;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Helpers;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Services;

internal sealed class LogExportService : ILogExportService
{
    private static readonly Dictionary<string, Func<ILogFormatter>> FormatterFactories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TXT"] = () => new LogFormatter(),
            ["JSON"] = () => new JsonLogFormatter()
        };

    public async Task<string> ExportLogsAsync(
        IEnumerable<LogEntry> logs,
        string format,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var logsList = logs as IReadOnlyList<LogEntry> ?? logs.ToList();
        if (logsList.Count == 0)
        {
            throw new InvalidOperationException("No logs to export.");
        }

        var normalizedFormat = format.ToUpperInvariant();
        if (!FormatterFactories.TryGetValue(normalizedFormat, out var formatterFactory))
        {
            throw new InvalidOperationException(
                $"Unknown format: {format}. Supported formats: {GetSupportedFormatsDescription()}");
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var formatter = formatterFactory();
            var sortedLogs = logsList.OrderBy(log => log.Timestamp);
            var formattedLines = sortedLogs.Select(log =>
            {
                var formattedResult = formatter.Format(log);
                return formattedResult.ToString() ?? string.Empty;
            });

            var content = string.Join(Environment.NewLine, formattedLines);
            var filePath = string.IsNullOrEmpty(fileName)
                ? LogPathHelper.GetLogFilePath(normalizedFormat)
                : GetFilePathWithName(fileName, normalizedFormat);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, content);
            return filePath;
        }, cancellationToken);
    }

    public string GetSupportedFormatsDescription()
    {
        return string.Join(", ", FormatterFactories.Keys);
    }

    private static string GetFilePathWithName(string fileName, string format)
    {
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        if (!safeFileName.EndsWith($".{format}", StringComparison.OrdinalIgnoreCase))
        {
            safeFileName = $"{safeFileName}.{format}";
        }

        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AzioEventLogAnalyzer");

        return Path.Combine(basePath, safeFileName);
    }
}
