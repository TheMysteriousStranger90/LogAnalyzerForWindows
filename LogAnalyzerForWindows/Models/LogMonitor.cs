using System.Diagnostics;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

internal sealed class LogMonitor : ILogMonitor, IDisposable
{
    private HashSet<LogEntry> _lastProcessedLogs = [];
    private CancellationTokenSource? _cts;
    private volatile bool _isMonitoring;
    private bool _disposedValue;

    private const int PollingIntervalMs = 1000;
    private const int ErrorRetryDelayMs = 5000;

    public bool IsMonitoring => _isMonitoring;

    public event EventHandler<LogsChangedEventArgs>? LogsChanged;
    public event EventHandler? MonitoringStarted;
    public event EventHandler? MonitoringStopped;

    public void Monitor(ILogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (_isMonitoring) return;

        _lastProcessedLogs = [];
        _cts = new CancellationTokenSource();
        _isMonitoring = true;

        MonitoringStarted?.Invoke(this, EventArgs.Empty);

        _ = MonitorAsync(reader, _cts.Token);
    }

    private async Task MonitorAsync(ILogReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var currentLogs = await reader.ReadLogsAsync(cancellationToken).ConfigureAwait(false);

                    if (currentLogs.Count > 0)
                    {
                        var logsToProcess = currentLogs.ToList();
                        _ = Task.Run(() => LogsChanged?.Invoke(this, new LogsChangedEventArgs(logsToProcess)), cancellationToken);
                        _lastProcessedLogs = new HashSet<LogEntry>(currentLogs);
                    }
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Error reading logs: {ex.Message}");
                    await Task.Delay(ErrorRetryDelayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"Error reading logs: {ex.Message}");
                    await Task.Delay(ErrorRetryDelayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(PollingIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        finally
        {
            _isMonitoring = false;
            MonitoringStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopMonitoring()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                StopMonitoring();
                _cts?.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal sealed class LogsChangedEventArgs : EventArgs
{
    public IReadOnlyList<LogEntry> Logs { get; }

    public LogsChangedEventArgs(IReadOnlyList<LogEntry> logs)
    {
        Logs = logs ?? throw new ArgumentNullException(nameof(logs));
    }
}
