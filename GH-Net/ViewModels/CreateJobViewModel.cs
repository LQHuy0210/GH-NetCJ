using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Collections.ObjectModel;
using System.IO;

namespace GH_Net.WPF.ViewModels;

public sealed class CsvSelectionViewModel : ViewModelBase
{
    private readonly ICsvImportService _csv;
    private string _filePath = string.Empty;
    private string? _selectedColumn;
    private int _rowCount;

    public CsvSelectionViewModel(ICsvImportService csv) => _csv = csv;
    public ObservableCollection<string> Headers { get; } = [];
    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (SetProperty(ref _filePath, value))
                OnPropertyChanged(nameof(FileName));
        }
    }
    public string FileName => Path.GetFileName(FilePath);
    public string? SelectedColumn { get => _selectedColumn; set => SetProperty(ref _selectedColumn, value); }
    public int RowCount { get => _rowCount; private set => SetProperty(ref _rowCount, value); }
    public bool IsReady => FilePath.Length > 0 && SelectedColumn is not null && RowCount > 0;

    public async Task LoadAsync(string filePath)
    {
        FilePath = string.Empty; Headers.Clear(); SelectedColumn = null; RowCount = 0;
        try
        {
            CsvInspection inspection = await _csv.InspectAsync(filePath);
            FilePath = inspection.FilePath;
            foreach (string header in inspection.Headers) Headers.Add(header);
            SelectedColumn = Headers.FirstOrDefault();
            RowCount = inspection.DataRowCount;
        }
        finally { OnPropertyChanged(nameof(IsReady)); }
    }
}

public sealed class CreateJobViewModel : ViewModelBase
{
    private readonly IPackingJobService _packingJobs;
    private readonly LanguageService _language;
    private string _jobName = string.Empty;
    private string _productsPerCarton = "20";
    private string _cartonsPerPallet = "50";
    private bool _useAutomaticName;
    private bool _isBusy;
    private DateTime _automaticNameTimestamp;

    public CreateJobViewModel(
        IPackingJobService packingJobs,
        ICsvImportService csv,
        LanguageService language)
    {
        _packingJobs = packingJobs; _language = language;
        Products = new CsvSelectionViewModel(csv);
        Cartons = new CsvSelectionViewModel(csv);
        Pallets = new CsvSelectionViewModel(csv);
        GenerateAutomaticName();
    }

    public CsvSelectionViewModel Products { get; }
    public CsvSelectionViewModel Cartons { get; }
    public CsvSelectionViewModel Pallets { get; }
    public Job? CreatedJob { get; private set; }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string JobName { get => _jobName; set { if (SetProperty(ref _jobName, value)) _useAutomaticName = false; } }
    public string ProductsPerCarton { get => _productsPerCarton; set => SetProperty(ref _productsPerCarton, value); }
    public string CartonsPerPallet { get => _cartonsPerPallet; set => SetProperty(ref _cartonsPerPallet, value); }

    public void GenerateAutomaticName()
    {
        _automaticNameTimestamp = DateTime.Now;
        _jobName = $"{_automaticNameTimestamp:yyMMdd_HHmmss}_Job<ID>";
        _useAutomaticName = true;
        OnPropertyChanged(nameof(JobName));
    }

    public async Task<(Job? Job, string? Error)> CreateAsync()
    {
        if (!_useAutomaticName && string.IsNullOrWhiteSpace(JobName)) return (null, _language.Get("Create.EnterName"));
        if (!Products.IsReady || !Cartons.IsReady || !Pallets.IsReady) return (null, _language.Get("Create.CsvRequired"));
        if (!int.TryParse(ProductsPerCarton, out int productCapacity) || productCapacity <= 0)
            return (null, _language.Get("Create.InvalidProductCapacity"));
        if (!int.TryParse(CartonsPerPallet, out int cartonCapacity) || cartonCapacity <= 0)
            return (null, _language.Get("Create.InvalidPalletCapacity"));

        IsBusy = true;
        try
        {
            CreatedJob = await _packingJobs.CreateAsync(new CreatePackingJobRequest
            {
                Name = _useAutomaticName ? null : JobName.Trim(),
                UseAutomaticName = _useAutomaticName,
                AutomaticNameTimestamp = _useAutomaticName ? _automaticNameTimestamp : null,
                ProductsPerCarton = productCapacity,
                CartonsPerPallet = cartonCapacity,
                Products = new PackingCsvSelection(Products.FilePath, Products.SelectedColumn!),
                Cartons = new PackingCsvSelection(Cartons.FilePath, Cartons.SelectedColumn!),
                Pallets = new PackingCsvSelection(Pallets.FilePath, Pallets.SelectedColumn!)
            });
            return (CreatedJob, null);
        }
        catch (Exception exception) { return (null, exception.Message); }
        finally { IsBusy = false; }
    }
}
