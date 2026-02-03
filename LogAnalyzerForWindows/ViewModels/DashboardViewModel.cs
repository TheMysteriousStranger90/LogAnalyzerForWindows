using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;
using SkiaSharp;

namespace LogAnalyzerForWindows.ViewModels;

internal sealed class DashboardViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ILogStatisticsService _statisticsService;
    private CancellationTokenSource? _loadingCts;
    private bool _isLoading;
    private string? _selectedSession;
    private LogStatistics? _statistics;
    private string _statusMessage = "Ready";
    private bool _disposedValue;

    private ISeries[] _levelPieSeries = [];
    private ISeries[] _timelineSeries = [];
    private ISeries[] _topSourcesSeries = [];
    private ISeries[] _topEventIdsSeries = [];

    private Axis[] _timelineXAxes = [];
    private Axis[] _timelineYAxes = [];
    private Axis[] _sourcesXAxes = [];
    private Axis[] _sourcesYAxes = [];
    private Axis[] _eventIdsXAxes = [];
    private Axis[] _eventIdsYAxes = [];

    public DashboardViewModel(ILogStatisticsService statisticsService)
    {
        _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));

        RefreshCommand = new AsyncRelayCommand(
            async () => await LoadDashboardDataAsync().ConfigureAwait(false),
            () => !IsLoading);

        InitializeChartAxes();
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.OnCanExecuteChanged();
            }
        }
    }

    public string? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                _ = LoadDashboardDataAsync();
            }
        }
    }

    public AvaloniaList<string> AvailableSessions { get; } = new() { "All Sessions" };

    public LogStatistics? Statistics
    {
        get => _statistics;
        private set => SetProperty(ref _statistics, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int TotalLogs => _statistics?.TotalLogs ?? 0;
    public int ErrorCount => _statistics?.ErrorCount ?? 0;
    public int WarningCount => _statistics?.WarningCount ?? 0;
    public int InformationCount => _statistics?.InformationCount ?? 0;

    public ISeries[] LevelPieSeries
    {
        get => _levelPieSeries;
        private set => SetProperty(ref _levelPieSeries, value);
    }

    public ISeries[] TimelineSeries
    {
        get => _timelineSeries;
        private set => SetProperty(ref _timelineSeries, value);
    }

    public Axis[] TimelineXAxes
    {
        get => _timelineXAxes;
        private set => SetProperty(ref _timelineXAxes, value);
    }

    public Axis[] TimelineYAxes
    {
        get => _timelineYAxes;
        private set => SetProperty(ref _timelineYAxes, value);
    }

    public Axis[] EventIdsXAxes
    {
        get => _eventIdsXAxes;
        private set => SetProperty(ref _eventIdsXAxes, value);
    }

    public Axis[] EventIdsYAxes
    {
        get => _eventIdsYAxes;
        private set => SetProperty(ref _eventIdsYAxes, value);
    }

    public ISeries[] TopSourcesSeries
    {
        get => _topSourcesSeries;
        private set => SetProperty(ref _topSourcesSeries, value);
    }

    public Axis[] SourcesXAxes
    {
        get => _sourcesXAxes;
        private set => SetProperty(ref _sourcesXAxes, value);
    }

    public Axis[] SourcesYAxes
    {
        get => _sourcesYAxes;
        private set => SetProperty(ref _sourcesYAxes, value);
    }

    public ISeries[] TopEventIdsSeries
    {
        get => _topEventIdsSeries;
        private set => SetProperty(ref _topEventIdsSeries, value);
    }

    public ICommand RefreshCommand { get; }

    private void InitializeChartAxes()
    {
        TimelineXAxes =
        [
            new Axis
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                LabelsRotation = 45
            }
        ];

        TimelineYAxes =
        [
            new Axis
            {
                Name = "Count",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                MinLimit = 0
            }
        ];

        SourcesXAxes =
        [
            new Axis
            {
                Labels = [],
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                LabelsRotation = 45
            }
        ];

        SourcesYAxes =
        [
            new Axis
            {
                Name = "Count",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                MinLimit = 0
            }
        ];

        EventIdsYAxes =
        [
            new Axis
            {
                Labels = [],
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                TextSize = 12
            }
        ];

        EventIdsXAxes =
        [
            new Axis
            {
                Name = "Count",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                MinLimit = 0
            }
        ];
    }

    public async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _statisticsService.GetSessionsAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableSessions.Clear();
                AvailableSessions.Add("All Sessions");
                foreach (var session in sessions)
                {
                    AvailableSessions.Add(session);
                }

                if (string.IsNullOrEmpty(SelectedSession))
                {
                    SelectedSession = "All Sessions";
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading sessions: {ex.Message}");
        }
    }

    public async Task LoadDashboardDataAsync()
    {
        if (_loadingCts != null)
        {
            await _loadingCts.CancelAsync().ConfigureAwait(false);
            _loadingCts.Dispose();
        }

        _loadingCts = new CancellationTokenSource();
        var token = _loadingCts.Token;

        IsLoading = true;
        StatusMessage = "Loading dashboard data...";

        try
        {
            var sessionId = SelectedSession == "All Sessions" ? null : SelectedSession;

            var statsTask = _statisticsService.GetStatisticsAsync(sessionId, token);
            var timeSeriesTask = _statisticsService.GetTimeSeriesAsync(sessionId, TimeSpan.FromHours(1), token);
            var topSourcesTask = _statisticsService.GetTopSourcesAsync(10, sessionId, token);
            var topEventIdsTask = _statisticsService.GetTopEventIdsAsync(10, sessionId, token);

            await Task.WhenAll(statsTask, timeSeriesTask, topSourcesTask, topEventIdsTask).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            var stats = await statsTask;
            var timeSeries = await timeSeriesTask;
            var topSources = await topSourcesTask;
            var topEventIds = await topEventIdsTask;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Statistics = stats;
                UpdateLevelPieChart(stats);
                UpdateTimelineChart(timeSeries);
                UpdateTopSourcesChart(topSources);
                UpdateTopEventIdsChart(topEventIds);

                OnPropertiesChanged(nameof(TotalLogs), nameof(ErrorCount), nameof(WarningCount),
                    nameof(InformationCount));

                StatusMessage = $"Dashboard loaded. Total: {stats.TotalLogs} logs";
            });
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Dashboard loading was cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading dashboard: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => StatusMessage = $"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void InvalidateCache()
    {
        _statisticsService.InvalidateCache();
    }

    private void UpdateLevelPieChart(LogStatistics stats)
    {
        var data = new List<(string Name, int Value, SKColor Color)>
        {
            ("Error", stats.ErrorCount, SKColors.Red),
            ("Warning", stats.WarningCount, SKColors.Orange),
            ("Information", stats.InformationCount, SKColors.DodgerBlue),
            ("Audit Success", stats.AuditSuccessCount, SKColors.LimeGreen),
            ("Audit Failure", stats.AuditFailureCount, SKColors.DarkOrange),
            ("Other", stats.OtherCount, SKColors.Gray)
        };

        var filteredData = data.Where(d => d.Value > 0).ToList();

        if (filteredData.Count == 0)
        {
            LevelPieSeries = [];
            return;
        }

        LevelPieSeries = filteredData
            .Select(d => new PieSeries<ObservableValue>
            {
                Name = d.Name,
                Values = new ObservableValue[] { new(d.Value) },
                Fill = new SolidColorPaint(d.Color),
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsSize = 12,
                DataLabelsFormatter = point => $"{d.Name} {d.Value}"
            })
            .Cast<ISeries>()
            .ToArray();
    }

    private void UpdateTimelineChart(List<TimeSeriesPoint> timeSeries)
    {
        if (timeSeries.Count == 0)
        {
            TimelineSeries = [];
            return;
        }

        var values = timeSeries
            .Select(p => new DateTimePoint(p.Time, p.Count))
            .ToList();

        TimelineSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Log Events",
                Values = values,
                Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                GeometryStroke = new SolidColorPaint(SKColors.White, 1),
                GeometrySize = 6,
                LineSmoothness = 0.3
            }
        ];

        TimelineXAxes =
        [
            new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("HH:mm", CultureInfo.InvariantCulture))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                LabelsRotation = 45
            }
        ];
    }

    private void UpdateTopSourcesChart(List<(string Source, int Count)> topSources)
    {
        if (topSources.Count == 0)
        {
            TopSourcesSeries = [];
            return;
        }

        var labels = topSources.Select(s => TruncateString(s.Source, 20)).ToArray();
        var values = topSources.Select(s => s.Count).ToArray();

        TopSourcesSeries =
        [
            new ColumnSeries<int>
            {
                Name = "Events",
                Values = values,
                Fill = new SolidColorPaint(SKColors.MediumPurple),
                Stroke = null,
                MaxBarWidth = 40
            }
        ];

        SourcesXAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                LabelsRotation = 45
            }
        ];
    }

    private void UpdateTopEventIdsChart(List<(int EventId, int Count)> topEventIds)
    {
        if (topEventIds.Count == 0)
        {
            TopEventIdsSeries = [];
            EventIdsYAxes = [new Axis { Labels = [] }];
            return;
        }

        var labels = topEventIds.Select(e => $"ID: {e.EventId}").ToArray();
        var values = topEventIds.Select(e => e.Count).ToArray();

        TopEventIdsSeries =
        [
            new RowSeries<int>
            {
                Name = "Event Count",
                Values = values,
                Fill = new SolidColorPaint(SKColors.Coral),
                Stroke = null,
                MaxBarWidth = 25,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.End,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString(CultureInfo.InvariantCulture)
            }
        ];

        EventIdsYAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                TextSize = 11
            }
        ];

        EventIdsXAxes =
        [
            new Axis
            {
                Name = "Count",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                MinLimit = 0
            }
        ];
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (!_disposedValue)
        {
            if (_loadingCts != null)
            {
                await _loadingCts.CancelAsync().ConfigureAwait(false);
                _loadingCts.Dispose();
                _loadingCts = null;
            }

            _disposedValue = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
