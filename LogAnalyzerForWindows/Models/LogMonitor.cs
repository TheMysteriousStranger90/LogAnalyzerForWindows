using System.Diagnostics;
using System.Threading.Channels;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

internal sealed class LogMonitor : ILogMonitor, IDisposable
{
    private readonly Channel<IReadOnlyList<LogEntry>> _logChannel;
    private CancellationTokenSource? _cts;
    private volatile bool _isMonitoring;
    private bool _disposedValue;

    private const int PollingIntervalMs = 1000;
    private const int ErrorRetryDelayMs = 5000;
    private const int ChannelCapacity = 100;

    public bool IsMonitoring => _isMonitoring;

    public event EventHandler<LogsChangedEventArgs>? LogsChanged;
    public event EventHandler? MonitoringStarted;
    public event EventHandler? MonitoringStopped;

    public LogMonitor()
    {
        _logChannel = Channel.CreateBounded<IReadOnlyList<LogEntry>>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
    }

    public void Monitor(ILogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (_isMonitoring) return;

        _cts = new CancellationTokenSource();
        _isMonitoring = true;

        MonitoringStarted?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(() => ProduceLogsAsync(reader, _cts.Token));
        _ = Task.Run(() => ConsumeLogsAsync(_cts.Token));
    }

    private async Task ProduceLogsAsync(ILogReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var logs = await reader.ReadLogsAsync(cancellationToken).ConfigureAwait(false);

                    if (logs.Count > 0)
                    {
                        await _logChannel.Writer.WriteAsync(logs, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"Error reading logs: {ex.Message}");
                    try
                    {
                        await Task.Delay(ErrorRetryDelayMs, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                try
                {
                    await Task.Delay(PollingIntervalMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            _logChannel.Writer.TryComplete();
        }
    }

    private async Task ConsumeLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var logs in _logChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    LogsChanged?.Invoke(this, new LogsChangedEventArgs(logs));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in LogsChanged handler: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
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
