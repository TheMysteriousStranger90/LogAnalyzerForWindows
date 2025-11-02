using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

/// <summary>
/// Defines the contract for managing and processing log entries.
/// </summary>
/// <remarks>
/// This interface coordinates the log processing pipeline by orchestrating
/// the analyzer, formatter, and writer components to transform raw log entries
/// into formatted output.
/// </remarks>
internal interface ILogManager
{
    /// <summary>
    /// Processes a collection of log entries through the analysis, formatting, and writing pipeline.
    /// </summary>
    /// <param name="logs">The collection of log entries to process.</param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while processing logs. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>A task representing the asynchronous processing operation.</returns>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item><description>Analyzes logs using the configured analyzer</description></item>
    /// <item><description>Formats each log entry using the configured formatter</description></item>
    /// <item><description>Writes formatted logs using the configured writer</description></item>
    /// <item><description>Processes logs in batches to optimize performance</description></item>
    /// <item><description>Supports cancellation via the provided token</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when log analysis fails due to invalid data.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the log processing pipeline encounters an invalid state.</exception>
    /// <exception cref="IOException">Thrown when writing logs fails due to I/O errors.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    Task ProcessLogsAsync(IEnumerable<LogEntry> logs, CancellationToken cancellationToken = default);
}
