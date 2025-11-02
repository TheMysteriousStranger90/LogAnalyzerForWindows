using LogAnalyzerForWindows.Models;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Interfaces;

/// <summary>
/// Defines the contract for monitoring log sources in real-time.
/// </summary>
/// <remarks>
/// This interface provides functionality to continuously monitor log sources
/// and notify subscribers when new log entries are detected. It implements
/// a polling-based monitoring strategy with configurable intervals.
/// </remarks>
internal interface ILogMonitor
{
    /// <summary>
    /// Occurs when new log entries are detected during monitoring.
    /// </summary>
    /// <remarks>
    /// This event is raised whenever the monitor detects changes in the log source.
    /// Subscribers receive a <see cref="LogsChangedEventArgs"/> containing the new log entries.
    /// The event may be raised on a background thread, so subscribers should handle
    /// thread synchronization if updating UI components.
    /// </remarks>
    event EventHandler<LogsChangedEventArgs> LogsChanged;

    /// <summary>
    /// Starts monitoring the specified log reader for new log entries.
    /// </summary>
    /// <param name="reader">The log reader instance to monitor for new entries.</param>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item><description>Initiates continuous monitoring on a background thread</description></item>
    /// <item><description>Polls the reader at regular intervals (typically 1 second)</description></item>
    /// <item><description>Detects and reports new log entries via the <see cref="LogsChanged"/> event</description></item>
    /// <item><description>Continues until <see cref="StopMonitoring"/> is called or the monitor is disposed</description></item>
    /// <item><description>Handles I/O and access exceptions gracefully by continuing to monitor</description></item>
    /// </list>
    /// If monitoring is already active, this method has no effect.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is null.</exception>
    void Monitor(ILogReader reader);

    /// <summary>
    /// Stops the active monitoring operation.
    /// </summary>
    /// <remarks>
    /// This method gracefully stops the monitoring loop initiated by <see cref="Monitor"/>.
    /// If monitoring is not currently active, this method has no effect.
    /// After stopping, the monitor can be restarted by calling <see cref="Monitor"/> again.
    /// </remarks>
    void StopMonitoring();
}
