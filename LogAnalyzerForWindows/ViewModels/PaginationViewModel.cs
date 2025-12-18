using System.ComponentModel;
using System.Diagnostics;
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
    private string? _searchText;
    private int? _eventIdFilter;
    private string? _sourceFilter;

    public AvaloniaList<LogEntry> CurrentPageLogs { get; private set; } = new();
    public AvaloniaList<int> PageSizes { get; } = new() { 25, 50, 100, 200, 500 };
    public AvaloniaList<string> AvailableSources { get; } = new();
    public AvaloniaList<int> AvailableEventIds { get; } = new();

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

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasActiveFilters));
            }
        }
    }

    public int? EventIdFilter
    {
        get => _eventIdFilter;
        set
        {
            if (SetProperty(ref _eventIdFilter, value))
            {
                OnPropertyChanged(nameof(HasActiveFilters));
                CurrentPage = 1;
                _ = LoadLogsAsync().ConfigureAwait(false);
            }
        }
    }

    public string? SourceFilter
    {
        get => _sourceFilter;
        set
        {
            if (SetProperty(ref _sourceFilter, value))
            {
                OnPropertyChanged(nameof(HasActiveFilters));
                CurrentPage = 1;
                _ = LoadLogsAsync().ConfigureAwait(false);
            }
        }
    }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(_searchText) ||
        _eventIdFilter.HasValue ||
        !string.IsNullOrWhiteSpace(_sourceFilter);

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
    public ICommand SearchCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public PaginationViewModel(ILogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        FirstPageCommand = new RelayCommand(() => CurrentPage = 1, () => CurrentPage > 1);
        PreviousPageCommand = new RelayCommand(() => CurrentPage--, () => CurrentPage > 1);
        NextPageCommand = new RelayCommand(() => CurrentPage++, () => CurrentPage < TotalPages);
        LastPageCommand = new RelayCommand(() => CurrentPage = TotalPages, () => CurrentPage < TotalPages);
        RefreshCommand = new RelayCommand(async () => await LoadLogsAsync().ConfigureAwait(false));
        SearchCommand = new RelayCommand(ExecuteSearch);
        ClearFiltersCommand = new RelayCommand(ClearAllFilters);
    }

    private void ExecuteSearch()
    {
        CurrentPage = 1;
        _ = LoadLogsAsync().ConfigureAwait(false);
    }

    private void ClearAllFilters()
    {
        _searchText = null;
        _eventIdFilter = null;
        _sourceFilter = null;
        _levelFilter = null;

        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(EventIdFilter));
        OnPropertyChanged(nameof(SourceFilter));
        OnPropertyChanged(nameof(HasActiveFilters));

        CurrentPage = 1;
        _ = LoadLogsAsync().ConfigureAwait(false);
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
                _currentSessionId,
                _searchText,
                _eventIdFilter,
                _sourceFilter).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentPageLogs.Clear();
                foreach (var log in logs)
                {
                    CurrentPageLogs.Add(log);
                }
            });

            TotalRecords = totalCount;
            TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));

            OnPropertyChanged(nameof(PageInfo));
            UpdateCommandStates();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Invalid operation while loading logs: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error while loading logs: {ex.Message}");
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine($"Loading logs was cancelled: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadFilterOptionsAsync()
    {
        try
        {
            var sources = await _repository.GetDistinctSourcesAsync(_currentSessionId).ConfigureAwait(false);
            var eventIds = await _repository.GetDistinctEventIdsAsync(_currentSessionId).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableSources.Clear();
                AvailableSources.Add(string.Empty);
                foreach (var source in sources)
                {
                    AvailableSources.Add(source);
                }

                AvailableEventIds.Clear();
                foreach (var eventId in eventIds)
                {
                    AvailableEventIds.Add(eventId);
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Invalid operation while loading filter options: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error while loading filter options: {ex.Message}");
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine($"Loading filter options was cancelled: {ex.Message}");
        }
    }

    public void SetFilters(string? level = null, DateTime? start = null, DateTime? end = null, string? sessionId = null)
    {
        _levelFilter = level;
        _startDate = start;
        _endDate = end;
        _currentSessionId = sessionId;
        CurrentPage = 1;

        _ = LoadFilterOptionsAsync().ConfigureAwait(false);
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
