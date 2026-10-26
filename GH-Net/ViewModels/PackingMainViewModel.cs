using GH_Net.Core.Enums;
using GH_Net.Licensing;
using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Collections.ObjectModel;
using System.Windows;

namespace GH_Net.WPF.ViewModels;

public sealed class PackingMainViewModel : ViewModelBase, IDisposable
{
    private readonly IJobService _jobs; private readonly IJobRuntimeCoordinator _coordinator; private readonly IPackingRuntimeService _runtime; private readonly LanguageService _language; private readonly ILicenseManager _license;
    private int _jobId; private string _jobName = "", _workflow = "", _cartonCode = "-", _palletCode = "-", _message = "";
    private int _cartonCount, _cartonCapacity, _palletCount, _palletCapacity, _packed, _total;
    private int _checkedProducts, _remainingProducts, _checkedCartons, _remainingCartons, _checkedPallets, _remainingPallets;
    private JobStatus _status; private bool _busy, _retry;
    public PackingMainViewModel(IJobService jobs, IJobRuntimeCoordinator coordinator, IPackingRuntimeService runtime, LanguageService language, ILicenseManager license)
    {
        _jobs = jobs; _coordinator = coordinator; _runtime = runtime; _language = language; _license = license;
        StartCommand = new AsyncRelayCommand(() => RunAsync(() => _coordinator.StartAsync(_jobId)), () => !Busy && Status is JobStatus.NewlyCreated or JobStatus.Paused);
        StopCommand = new AsyncRelayCommand(() => RunAsync(() => _coordinator.StopAsync()), () => !Busy && Status == JobStatus.Running);
        RetryPrintCommand = new AsyncRelayCommand(() => RunAsync(() => _runtime.RetryPrintAsync()), () => !Busy && Status == JobStatus.Running && CanRetryPrint);
        BackCommand = new RelayCommand<object>(_ => RequestBack?.Invoke(), _ => !Busy && Status != JobStatus.Running);
        TraceCommand = new RelayCommand<object>(_ => RequestTrace?.Invoke(), _ => !Busy);
        ExportCommand = new RelayCommand<object>(_ => RequestExport?.Invoke(_jobId), _ => !Busy && Status != JobStatus.Running && _license.HasFeature(LicenseFeature.Export));
        _runtime.SnapshotChanged += OnSnapshot; _runtime.RuntimeFailed += OnFailure; _coordinator.StatusChanged += OnStatus;
    }
    public ObservableCollection<string> RecentProducts { get; } = [];
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RetryPrintCommand { get; }
    public RelayCommand<object> BackCommand { get; }
    public RelayCommand<object> TraceCommand { get; }
    public RelayCommand<object> ExportCommand { get; }
    public Action? RequestBack { get; set; }
    public Action? RequestTrace { get; set; }
    public Action<int>? RequestExport { get; set; }
    public string JobName { get => _jobName; private set => SetProperty(ref _jobName, value); }
    public JobStatus Status { get => _status; private set { if (SetProperty(ref _status, value)) { OnPropertyChanged(nameof(StatusText)); Refresh(); } } }
    public string ModeText { get; private set; } = "";
    public string StatusText => Status.ToString(); public string Workflow { get => _workflow; private set => SetProperty(ref _workflow, value); }
    public string CartonCode { get => _cartonCode; private set => SetProperty(ref _cartonCode, value); }
    public string PalletCode { get => _palletCode; private set => SetProperty(ref _palletCode, value); }
    public int CartonCount { get => _cartonCount; private set => SetProperty(ref _cartonCount, value); }
    public int CartonCapacity { get => _cartonCapacity; private set => SetProperty(ref _cartonCapacity, value); }
    public int PalletCount { get => _palletCount; private set => SetProperty(ref _palletCount, value); }
    public int PalletCapacity { get => _palletCapacity; private set => SetProperty(ref _palletCapacity, value); }
    public int PackedProducts { get => _packed; private set => SetProperty(ref _packed, value); }
    public int TotalProducts { get => _total; private set => SetProperty(ref _total, value); }
    public int CheckedProducts { get => _checkedProducts; private set => SetProperty(ref _checkedProducts, value); }
    public int RemainingProducts { get => _remainingProducts; private set => SetProperty(ref _remainingProducts, value); }
    public int CheckedCartons { get => _checkedCartons; private set => SetProperty(ref _checkedCartons, value); }
    public int RemainingCartons { get => _remainingCartons; private set => SetProperty(ref _remainingCartons, value); }
    public int CheckedPallets { get => _checkedPallets; private set => SetProperty(ref _checkedPallets, value); }
    public int RemainingPallets { get => _remainingPallets; private set => SetProperty(ref _remainingPallets, value); }
    public string Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool Busy { get => _busy; private set { if (SetProperty(ref _busy, value)) Refresh(); } }
    public bool CanRetryPrint { get => _retry; private set { if (SetProperty(ref _retry, value)) Refresh(); } }
    public async Task LoadAsync(int jobId)
    {
        _jobId = jobId;
        var job = await _jobs.GetByIdWithDetailsAsync(jobId) ?? throw new InvalidOperationException("Job not found.");
        PackingCodeStatistics statistics = await _jobs.GetPackingCodeStatisticsAsync(jobId);
        ModeText = _language.Get(job.Mode == PackingMode.SemiAutomatic ? "Automation.SemiMode" : "Automation.Manual") + (job.UsePlc ? " · PLC" : "");
        OnPropertyChanged(nameof(ModeText));
        JobName = job.Name;
        Status = job.Status;
        Workflow = job.PackingWorkflowState.ToString();
        TotalProducts = job.TotalData;
        CartonCapacity = job.ProductsPerCarton;
        PalletCapacity = job.CartonsPerPallet;
        ApplyStatistics(statistics);
        if (_runtime.ActiveJobId == jobId && _runtime.Current is not null) Apply(_runtime.Current);
    }
    private async Task RunAsync(Func<Task> action) { Busy = true; Message = ""; try { await action(); } catch (Exception ex) { Message = ex.Message; } finally { Busy = false; } }
    private void OnSnapshot(object? s, PackingRuntimeSnapshot x) => Dispatch(() => Apply(x));
    private void Apply(PackingRuntimeSnapshot x)
    {
        if (x.JobId != _jobId) return;
        Workflow = x.WorkflowState.ToString();
        CartonCode = x.CartonCode ?? "-";
        CartonCount = x.CartonCount;
        CartonCapacity = x.ProductsPerCarton;
        PalletCode = x.PalletCode ?? "-";
        PalletCount = x.PalletCount;
        PalletCapacity = x.CartonsPerPallet;
        PackedProducts = x.PackedProducts;
        TotalProducts = x.TotalProducts;
        CheckedProducts = x.CheckedProducts;
        RemainingProducts = x.RemainingProducts;
        CheckedCartons = x.CheckedCartons;
        RemainingCartons = x.RemainingCartons;
        CheckedPallets = x.CheckedPallets;
        RemainingPallets = x.RemainingPallets;
        Message = x.Message;
        CanRetryPrint = x.CanRetryPrint;
        RecentProducts.Clear();
        foreach (var p in x.RecentProducts) RecentProducts.Add(p);
        if (x.WorkflowState == PackingWorkflowState.Completed) Status = JobStatus.Completed;
    }

    private void ApplyStatistics(PackingCodeStatistics statistics)
    {
        CheckedProducts = statistics.CheckedProducts;
        RemainingProducts = statistics.RemainingProducts;
        CheckedCartons = statistics.CheckedCartons;
        RemainingCartons = statistics.RemainingCartons;
        CheckedPallets = statistics.CheckedPallets;
        RemainingPallets = statistics.RemainingPallets;
    }
    private void OnStatus(JobStatus status) => Dispatch(() => Status = status); private void OnFailure(object? s, Exception ex) => Dispatch(() => Message = ex.Message);
    private static void Dispatch(Action action) { var d = Application.Current?.Dispatcher; if (d is null || d.CheckAccess()) action(); else d.BeginInvoke(action); }
    private void Refresh() { StartCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); RetryPrintCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged(); TraceCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged(); }
    public void Dispose() { _runtime.SnapshotChanged -= OnSnapshot; _runtime.RuntimeFailed -= OnFailure; _coordinator.StatusChanged -= OnStatus; }
}
