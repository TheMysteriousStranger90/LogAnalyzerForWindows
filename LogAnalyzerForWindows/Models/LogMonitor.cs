using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models;

public class LogMonitor : ILogMonitor
{
    private HashSet<LogEntry> _lastProcessedLogs = new HashSet<LogEntry>();
    public event Action<IEnumerable<LogEntry>> LogsChanged;
    public event Action MonitoringStarted;
    public event Action MonitoringStopped;
    private CancellationTokenSource _cts;
    private volatile bool _isMonitoring;

    public bool IsMonitoring => _isMonitoring;

    public void Monitor(ILogReader reader)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        if (_isMonitoring) return;

        _cts = new CancellationTokenSource();
        _isMonitoring = true;
        MonitoringStarted?.Invoke();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                List<LogEntry> currentLogs;
                try
                {
                    currentLogs = reader.ReadLogs().ToList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading logs: {ex.Message}");
                    Thread.Sleep(5000);
                    continue;
                }

                var newLogs = currentLogs.Except(_lastProcessedLogs).ToList();

                if (newLogs.Any())
                {
                    LogsChanged?.Invoke(newLogs);
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
            MonitoringStopped?.Invoke();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void StopMonitoring()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }
}