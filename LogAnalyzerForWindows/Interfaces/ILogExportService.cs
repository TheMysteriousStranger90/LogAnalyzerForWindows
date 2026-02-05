using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ILogExportService
{
    Task<string> ExportLogsAsync(
        IEnumerable<LogEntry> logs,
        string format,
        string? fileName = null,
        CancellationToken cancellationToken = default);

    string GetSupportedFormatsDescription();
}
