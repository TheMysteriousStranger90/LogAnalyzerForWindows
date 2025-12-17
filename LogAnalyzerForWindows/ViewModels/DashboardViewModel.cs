using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Database.Repositories;
using LogAnalyzerForWindows.Models;
using SkiaSharp;

namespace LogAnalyzerForWindows.ViewModels;

internal sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly ILogRepository _repository;
    private bool _isLoading;
    private string? _selectedSession;
    private LogStatistics? _statistics;
    private string _statusMessage = "Ready";

    private ISeries[] _levelPieSeries = [];
    private ISeries[] _timelineSeries = [];
    private ISeries[] _topSourcesSeries = [];
    private ISeries[] _topEventIdsSeries = [];

    private Axis[] _timelineXAxes = [];
    private Axis[] _timelineYAxes = [];
    private Axis[] _sourcesXAxes = [];
    private Axis[] _sourcesYAxes = [];

    public DashboardViewModel(ILogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        RefreshCommand = new RelayCommand(
            async () => await LoadDashboardDataAsync().ConfigureAwait(false),
            () => !IsLoading);

        InitializeChartAxes();
    }

    #region Properties

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                (RefreshCommand as RelayCommand)?.OnCanExecuteChanged();
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

    #endregion

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
    }

    public async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _repository.GetSessionIdsAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableSessions.Clear();
                AvailableSessions.Add("All Sessions");
                foreach (var session in sessions)
                {
                    AvailableSessions.Add(session);
                }

                SelectedSession = "All Sessions";
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading sessions: {ex.Message}");
        }
    }

    public async Task LoadDashboardDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading dashboard data...";

        try
        {
            var sessionId = SelectedSession == "All Sessions" ? null : SelectedSession;

            var stats = await _repository.GetDetailedStatisticsAsync(sessionId).ConfigureAwait(false);
            Statistics = stats;

            var timeSeries = await _repository.GetLogsTimeSeriesAsync(
                sessionId,
                groupBy: TimeSpan.FromHours(1)).ConfigureAwait(false);

            var topSources = await _repository.GetTopSourcesAsync(10, sessionId).ConfigureAwait(false);

            var topEventIds = await _repository.GetTopEventIdsAsync(10, sessionId).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateLevelPieChart(stats);
                UpdateTimelineChart(timeSeries);
                UpdateTopSourcesChart(topSources);
                UpdateTopEventIdsChart(topEventIds);

                OnPropertyChanged(nameof(TotalLogs));
                OnPropertyChanged(nameof(ErrorCount));
                OnPropertyChanged(nameof(WarningCount));
                OnPropertyChanged(nameof(InformationCount));

                StatusMessage = $"Dashboard loaded. Total: {stats.TotalLogs} logs";
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading dashboard: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => { StatusMessage = $"Error: {ex.Message}"; });
        }
        finally
        {
            IsLoading = false;
        }
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

        LevelPieSeries = data
            .Where(d => d.Value > 0)
            .Select(d => new PieSeries<int>
            {
                Name = d.Name,
                Values = new[] { d.Value },
                Fill = new SolidColorPaint(d.Color),
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsFormatter = point => $"{d.Name}: {point.Coordinate.PrimaryValue}"
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
            return;
        }

        var data = topEventIds
            .Select(e => new { Label = $"ID: {e.EventId}", Value = e.Count })
            .ToList();

        TopEventIdsSeries =
        [
            new RowSeries<int>
            {
                Name = "Event Count",
                Values = data.Select(d => d.Value).ToArray(),
                Fill = new SolidColorPaint(SKColors.Coral),
                Stroke = null,
                MaxBarWidth = 25,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.End
            }
        ];
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
