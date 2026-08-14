using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Collections.ObjectModel;
using System.IO;

namespace GH_Net.WPF.ViewModels;

public sealed class PackingExportViewModel : ViewModelBase, IDisposable
{
    private readonly IPackingExportService _service;
    private readonly IJobService _jobs;
    private readonly LanguageService _language;
    private int _jobId;
    private string _jobName = string.Empty;
    private string _message = string.Empty;
    private bool _busy;
    private bool _includeProducts = true;
    private bool _includeCartons = true;
    private bool _includePallets = true;
    private PackingExportOption? _selectedFormat;
    private PackingExportScopeOption? _selectedScope;
    private CancellationTokenSource? _cts;

    public PackingExportViewModel(
        IPackingExportService service,
        IJobService jobs,
        LanguageService language)
    {
        _service = service;
        _jobs = jobs;
        _language = language;
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
        CancelCommand = new RelayCommand<object>(_ => _cts?.Cancel(), _ => Busy);
        _language.LanguageChanged += OnLanguageChanged;
        RebuildOptions();
    }

    public ObservableCollection<PackingExportOption> Formats { get; } = [];
    public ObservableCollection<PackingExportScopeOption> Scopes { get; } = [];
    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand<object> CancelCommand { get; }
    public Func<string, PackingExportFormat, string?>? RequestDestinationPath { get; set; }

    public string JobName
    {
        get => _jobName;
        private set => SetProperty(ref _jobName, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
            {
                ExportCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public PackingExportOption? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
                ExportCommand.NotifyCanExecuteChanged();
        }
    }

    public PackingExportScopeOption? SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (!SetProperty(ref _selectedScope, value)) return;
            OnPropertyChanged(nameof(IsUnusedScope));
            OnPropertyChanged(nameof(Description));
            Message = string.Empty;
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsUnusedScope => SelectedScope?.Scope == PackingExportScope.UnusedCodes;
    public string Description => _language.Get(IsUnusedScope
        ? "PackingExport.UnusedDescription"
        : "PackingExport.FullDescription");

    public bool IncludeProducts
    {
        get => _includeProducts;
        set => SetSelection(ref _includeProducts, value, nameof(IncludeProducts));
    }

    public bool IncludeCartons
    {
        get => _includeCartons;
        set => SetSelection(ref _includeCartons, value, nameof(IncludeCartons));
    }

    public bool IncludePallets
    {
        get => _includePallets;
        set => SetSelection(ref _includePallets, value, nameof(IncludePallets));
    }

    public async Task LoadAsync(int jobId)
    {
        _jobId = jobId;
        JobName = (await _jobs.GetByIdWithDetailsAsync(jobId)
            ?? throw new InvalidOperationException("Job not found.")).Name;
    }

    private bool CanExport()
        => !Busy
           && SelectedFormat is not null
           && SelectedScope is not null
           && (!IsUnusedScope || SelectedCodes != PackingCodeSelection.None);

    private PackingCodeSelection SelectedCodes
    {
        get
        {
            PackingCodeSelection selection = PackingCodeSelection.None;
            if (IncludeProducts) selection |= PackingCodeSelection.Products;
            if (IncludeCartons) selection |= PackingCodeSelection.Cartons;
            if (IncludePallets) selection |= PackingCodeSelection.Pallets;
            return selection;
        }
    }

    private async Task ExportAsync()
    {
        PackingExportOption format = SelectedFormat!;
        PackingExportScope scope = SelectedScope!.Scope;
        PackingExportRequest request = new(format.Format, scope, SelectedCodes);
        string extension = format.Format == PackingExportFormat.Excel ? "xlsx" : "zip";
        string scopeName = scope == PackingExportScope.FullJob ? "Full" : "Unused";
        string fileName = Sanitize($"{JobName}_{scopeName}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
        string? selected = RequestDestinationPath?.Invoke(fileName, format.Format);
        if (string.IsNullOrWhiteSpace(selected)) return;

        string path = Path.GetFullPath(selected);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Invalid folder.");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(directory);
        _cts = new CancellationTokenSource();
        Busy = true;
        Message = _language.Get(scope == PackingExportScope.FullJob
            ? "PackingExport.ExportingFull"
            : "PackingExport.ExportingUnused");

        try
        {
            PackingExportResult result;
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                result = await _service.ExportAsync(_jobId, request, stream, _cts.Token);
            }

            File.Move(temporary, path, true);
            Message = scope == PackingExportScope.FullJob
                ? _language.Format("PackingExport.FullSuccess", result.Products, result.Cartons,
                    result.Pallets, result.ScanEvents, result.PrintAttempts)
                : _language.Format("PackingExport.UnusedSuccess", result.Products, result.Cartons, result.Pallets);
        }
        catch (OperationCanceledException)
        {
            Message = _language.Get("PackingExport.Cancelled");
        }
        catch (Exception ex)
        {
            Message = _language.Format("PackingExport.Failed", ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // Best-effort cleanup of an incomplete export.
            }

            _cts.Dispose();
            _cts = null;
            Busy = false;
        }
    }

    private void SetSelection(ref bool field, bool value, string propertyName)
    {
        if (!SetProperty(ref field, value, propertyName)) return;
        Message = IsUnusedScope && SelectedCodes == PackingCodeSelection.None
            ? _language.Get("PackingExport.SelectAtLeastOne")
            : string.Empty;
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void RebuildOptions()
    {
        PackingExportFormat selectedFormat = SelectedFormat?.Format ?? PackingExportFormat.Excel;
        PackingExportScope selectedScope = SelectedScope?.Scope ?? PackingExportScope.FullJob;

        Formats.Clear();
        Formats.Add(new(PackingExportFormat.Excel, _language.Get("PackingExport.Excel")));
        Formats.Add(new(PackingExportFormat.CsvZip, _language.Get("PackingExport.CsvZip")));
        SelectedFormat = Formats.Single(x => x.Format == selectedFormat);

        Scopes.Clear();
        Scopes.Add(new(PackingExportScope.FullJob, _language.Get("PackingExport.FullJob")));
        Scopes.Add(new(PackingExportScope.UnusedCodes, _language.Get("PackingExport.UnusedCodes")));
        SelectedScope = Scopes.Single(x => x.Scope == selectedScope);
        OnPropertyChanged(nameof(Description));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildOptions();

    private static string Sanitize(string value)
    {
        foreach (char character in Path.GetInvalidFileNameChars())
            value = value.Replace(character, '_');
        return value;
    }

    public void Dispose()
    {
        _language.LanguageChanged -= OnLanguageChanged;
        _cts?.Cancel();
    }
}

public sealed record PackingExportOption(PackingExportFormat Format, string Text);
public sealed record PackingExportScopeOption(PackingExportScope Scope, string Text);
