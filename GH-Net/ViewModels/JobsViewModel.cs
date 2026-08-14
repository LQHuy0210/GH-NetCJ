using GH_Net.Core.Enums;
using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace GH_Net.WPF.ViewModels;

/// <summary>View model for searching, selecting and managing print jobs.</summary>
public sealed class JobsViewModel : ViewModelBase
{
    private readonly IJobService _jobService;
    private readonly LanguageService _language;
    private string _searchText = string.Empty;
    private string _notificationMessage = string.Empty;
    private string _busyMessage = string.Empty;
    private bool _isBusy;
    private bool _isUpdatingSelection;

    public JobsViewModel(IJobService jobService, LanguageService language)
    {
        _jobService = jobService;
        _language = language;
        _language.LanguageChanged += OnLanguageChanged;
        FilteredJobs = CollectionViewSource.GetDefaultView(Jobs);
        FilteredJobs.Filter = FilterJob;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedCount > 0 && !IsBusy);
        CreateJobCommand = new RelayCommand<object>(_ => RequestCreateJob?.Invoke());
        OpenJobCommand = new RelayCommand<JobListItemViewModel>(
            job => RequestOpenJob?.Invoke(job!.Id),
            job => job is not null);
        ClearSearchCommand = new RelayCommand<object>(_ => SearchText = string.Empty);
    }

    public ObservableCollection<JobListItemViewModel> Jobs { get; } = [];
    public ICollectionView FilteredJobs { get; }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DeleteSelectedCommand { get; }
    public ICommand CreateJobCommand { get; }
    public ICommand OpenJobCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public Action? RequestCreateJob { get; set; }
    public Action<int>? RequestOpenJob { get; set; }
    public Func<
        IReadOnlyList<JobListItemViewModel>,
        Func<Task>,
        bool>? RequestDeleteConfirmation { get; set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
                return;

            FilteredJobs.Refresh();
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(HasVisibleJobs));
            OnPropertyChanged(nameof(IsAllVisibleSelected));
        }
    }

    public string NotificationMessage
    {
        get => _notificationMessage;
        private set => SetProperty(ref _notificationMessage, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetProperty(ref _busyMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            RefreshCommand.NotifyCanExecuteChanged();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public int TotalCount => Jobs.Count;
    public int VisibleCount => FilteredJobs.Cast<object>().Count();
    public int RunningCount => Jobs.Count(job => job.Status == JobStatus.Running);
    public int SelectedCount => Jobs.Count(job => job.IsSelected);
    public bool HasVisibleJobs => VisibleCount > 0;
    public string SelectionSummary => SelectedCount == 0
        ? _language.Get("Jobs.NoneSelected")
        : _language.Format("Jobs.Selected", SelectedCount);

    public bool IsAllVisibleSelected
    {
        get
        {
            var visible = FilteredJobs.Cast<JobListItemViewModel>().ToArray();
            return visible.Length > 0 && visible.All(job => job.IsSelected);
        }
        set
        {
            if (_isUpdatingSelection)
                return;

            _isUpdatingSelection = true;
            try
            {
                foreach (var job in FilteredJobs.Cast<JobListItemViewModel>())
                    job.IsSelected = value;
            }
            finally
            {
                _isUpdatingSelection = false;
                NotifySelectionChanged();
            }
        }
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        BusyMessage = _language.Get("Common.Processing");
        NotificationMessage = string.Empty;
        try
        {
            var jobs = await _jobService.GetAllAsync();
            Jobs.Clear();
            foreach (var job in jobs)
                Jobs.Add(new JobListItemViewModel(job, NotifySelectionChanged));

            FilteredJobs.Refresh();
            NotifyListChanged();
        }
        catch (Exception ex)
        {
            NotificationMessage = _language.Format("Jobs.LoadError", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectJob(int jobId)
    {
        var target = Jobs.FirstOrDefault(job => job.Id == jobId);
        if (target is not null)
            target.IsSelected = true;
    }

    private Task DeleteSelectedAsync()
    {
        var selected = Jobs.Where(job => job.IsSelected).ToArray();
        if (selected.Length == 0)
            return Task.CompletedTask;

        var running = selected.Where(job => job.Status == JobStatus.Running).ToArray();
        if (running.Length > 0)
        {
            NotificationMessage = _language.Format(
                "Jobs.DeleteRunning",
                string.Join(", ", running.Select(job => job.Name)));
            return Task.CompletedTask;
        }

        IsBusy = true;
        BusyMessage = _language.Get("Jobs.Deleting");
        try
        {
            var selectedIds = selected.Select(job => job.Id).ToArray();
            var deleted = RequestDeleteConfirmation?.Invoke(
                selected,
                () => Task.Run(
                    () => _jobService.DeleteJobsAsync(selectedIds))) == true;
            if (!deleted)
                return Task.CompletedTask;

            foreach (var job in selected)
                Jobs.Remove(job);

            FilteredJobs.Refresh();
            NotifyListChanged();
        }
        catch (Exception ex)
        {
            NotificationMessage = _language.Format("Jobs.DeleteError", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    private bool FilterJob(object item)
    {
        if (item is not JobListItemViewModel job)
            return false;

        var keyword = SearchText.Trim();
        return keyword.Length == 0 ||
               job.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               job.Status.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void NotifySelectionChanged()
    {
        if (_isUpdatingSelection)
            return;

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(IsAllVisibleSelected));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasVisibleJobs));
        OnPropertyChanged(nameof(IsAllVisibleSelected));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        FilteredJobs.Refresh();
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
