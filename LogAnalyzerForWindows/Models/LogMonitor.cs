using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

internal sealed class LogMonitor : ILogMonitor, IDisposable
{
    private HashSet<LogEntry> _lastProcessedLogs = [];

    public event EventHandler<LogsChangedEventArgs>? LogsChanged;
    public event EventHandler? MonitoringStarted;
    public event EventHandler? MonitoringStopped;

    private CancellationTokenSource? _cts;
    private volatile bool _isMonitoring;
    private bool _disposedValue;

    public bool IsMonitoring => _isMonitoring;

        public void Monitor(ILogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (_isMonitoring) return;

        _lastProcessedLogs = [];

        _cts = new CancellationTokenSource();
        _isMonitoring = true;
        MonitoringStarted?.Invoke(this, EventArgs.Empty);

        Task.Run(() =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    List<LogEntry> currentLogs;
                    try
                    {
                        currentLogs = reader.ReadLogs().ToList();
                    }
                    catch (IOException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading logs: {ex.Message}");
                        Thread.Sleep(5000);
                        continue;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading logs: {ex.Message}");
                        Thread.Sleep(5000);
                        continue;
                    }

                    var newLogs = currentLogs;
                    if (newLogs.Count > 0)
                    {
                        LogsChanged?.Invoke(this, new LogsChangedEventArgs(newLogs));
                        _lastProcessedLogs = new HashSet<LogEntry>(currentLogs);
                    }

                    try
                    {
                        bool cancelled = _cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        if (cancelled || _cts.Token.IsCancellationRequested) break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isMonitoring = false;
                MonitoringStopped?.Invoke(this, EventArgs.Empty);
                _cts?.Dispose();
                _cts = null;
            }
        });
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
    public IEnumerable<LogEntry> Logs { get; }

    public LogsChangedEventArgs(IEnumerable<LogEntry> logs)
    {
        Logs = logs;
    }
}
