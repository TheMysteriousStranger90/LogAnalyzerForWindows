using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Threading;
using LogAnalyzerForWindows.Commands;
using LogAnalyzerForWindows.Database.Repositories;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.ViewModels;

internal sealed class PaginationViewModel : INotifyPropertyChanged
{
    private readonly ILogRepository _repository;

    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _pageSize = 50;
    private int _totalRecords;
    private string? _levelFilter;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private string? _currentSessionId;

    public AvaloniaList<LogEntry> CurrentPageLogs { get; } = new();
    public AvaloniaList<int> PageSizes { get; } = new() { 25, 50, 100, 200, 500 };

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                _ = LoadLogsAsync().ConfigureAwait(false);
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => SetProperty(ref _totalPages, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                CurrentPage = 1;
                _ = LoadLogsAsync().ConfigureAwait(false);
            }
        }
    }

    public int TotalRecords
    {
        get => _totalRecords;
        private set => SetProperty(ref _totalRecords, value);
    }

    public string PageInfo => $"Page {CurrentPage} of {TotalPages} (Total: {TotalRecords} records)";

    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand RefreshCommand { get; }

    public PaginationViewModel(ILogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        FirstPageCommand = new RelayCommand(() => CurrentPage = 1, () => CurrentPage > 1);
        PreviousPageCommand = new RelayCommand(() => CurrentPage--, () => CurrentPage > 1);
        NextPageCommand = new RelayCommand(() => CurrentPage++, () => CurrentPage < TotalPages);
        LastPageCommand = new RelayCommand(() => CurrentPage = TotalPages, () => CurrentPage < TotalPages);
        RefreshCommand = new RelayCommand(async () => await LoadLogsAsync().ConfigureAwait(false));
    }

    public async Task LoadLogsAsync()
    {
        IsLoading = true;
        try
        {
            var (logs, totalCount) = await _repository.GetLogsAsync(
                CurrentPage,
                PageSize,
                _levelFilter,
                _startDate,
                _endDate,
                _currentSessionId).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentPageLogs.Clear();
                foreach (var log in logs)
                {
                    CurrentPageLogs.Add(log);
                }
            });

            TotalRecords = totalCount;
            TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

            OnPropertyChanged(nameof(PageInfo));
            UpdateCommandStates();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetFilters(string? level = null, DateTime? start = null, DateTime? end = null, string? sessionId = null)
    {
        _levelFilter = level;
        _startDate = start;
        _endDate = end;
        _currentSessionId = sessionId;
        CurrentPage = 1;
        _ = LoadLogsAsync().ConfigureAwait(false);
    }

    private void UpdateCommandStates()
    {
        (FirstPageCommand as RelayCommand)?.OnCanExecuteChanged();
        (PreviousPageCommand as RelayCommand)?.OnCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.OnCanExecuteChanged();
        (LastPageCommand as RelayCommand)?.OnCanExecuteChanged();
    }

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
}
