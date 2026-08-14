using GH_Net.Core.Enums;
using GH_Net.Repository.Entities;

namespace GH_Net.WPF.ViewModels;

/// <summary>Bindable presentation model for one row in JobsView.</summary>
public sealed class JobListItemViewModel : ViewModelBase
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public JobListItemViewModel(Job job, Action selectionChanged)
    {
        Id = job.Id;
        Name = job.Name;
        Status = job.Status;
        TotalData = job.TotalData;
        TotalPacked = job.Status == JobStatus.Completed ? job.TotalData : 0;
        CreatedAt = job.CreatedAt;
        _selectionChanged = selectionChanged;
    }

    public int Id { get; }
    public string Name { get; }
    public JobStatus Status { get; }
    public int TotalData { get; }
    public int TotalPacked { get; }
    public DateTime CreatedAt { get; }
    public string ProgressText => $"{TotalPacked:N0} / {TotalData:N0}";
    public double ProgressPercent => TotalData <= 0
        ? 0
        : Math.Clamp(TotalPacked * 100d / TotalData, 0, 100);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                _selectionChanged();
        }
    }
}
