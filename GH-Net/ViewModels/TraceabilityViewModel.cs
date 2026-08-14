using GH_Net.Core.Enums;
using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Collections.ObjectModel;

namespace GH_Net.WPF.ViewModels;

public sealed class TraceabilityViewModel : ViewModelBase
{
    private readonly ITraceabilityService _service;
    private readonly LanguageService _language;
    private string _code = string.Empty;
    private string _message = string.Empty;
    private bool _busy;
    private TraceResultViewModel? _selectedResult;

    public TraceabilityViewModel(ITraceabilityService service, LanguageService language)
    {
        _service = service;
        _language = language;
        SearchCommand = new AsyncRelayCommand(
            SearchAsync,
            () => !Busy && !string.IsNullOrWhiteSpace(Code));
    }

    public ObservableCollection<TraceResultViewModel> Results { get; } = [];
    public AsyncRelayCommand SearchCommand { get; }

    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, value))
                SearchCommand.NotifyCanExecuteChanged();
        }
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
                SearchCommand.NotifyCanExecuteChanged();
        }
    }

    public TraceResultViewModel? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (SetProperty(ref _selectedResult, value))
                OnPropertyChanged(nameof(HasSelectedResult));
        }
    }

    public bool HasSelectedResult => SelectedResult is not null;

    private async Task SearchAsync()
    {
        Busy = true;
        Message = string.Empty;
        Results.Clear();
        SelectedResult = null;

        try
        {
            IReadOnlyList<TraceabilityResult> found = await _service.SearchExactAsync(Code);
            foreach (TraceabilityResult result in found)
                Results.Add(TraceResultViewModel.From(result, _language));

            SelectedResult = Results.FirstOrDefault();
            Message = found.Count == 0
                ? _language.Get("Trace.NotFound")
                : _language.Format("Trace.ResultsFound", found.Count);
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}

public sealed class TraceResultViewModel
{
    private TraceResultViewModel(TraceabilityResult result, string typeText)
    {
        JobId = result.JobId;
        JobName = result.JobName;
        ResultType = result.MatchedType;
        TypeText = typeText;
        MatchedCode = result.MatchedCode;
        Pallet = result.Pallet;
        Carton = result.Carton;
        Product = result.Product;

        IEnumerable<string?> path = result.MatchedType switch
        {
            ScanCodeType.Product => [result.JobName, result.Pallet?.Code, result.Carton?.Code, result.Product?.Code],
            ScanCodeType.Carton => [result.JobName, result.Pallet?.Code, result.Carton?.Code],
            ScanCodeType.Pallet => [result.JobName, result.Pallet?.Code],
            _ => [result.JobName, result.MatchedCode]
        };
        Breadcrumb = string.Join("  ›  ", path.Where(item => !string.IsNullOrWhiteSpace(item)));
        DisplayLabel = $"{result.JobName}  ·  {typeText}  ·  {result.MatchedCode}";
    }

    public int JobId { get; }
    public string JobName { get; }
    public ScanCodeType ResultType { get; }
    public string TypeText { get; }
    public string MatchedCode { get; }
    public string DisplayLabel { get; }
    public string Breadcrumb { get; }
    public TracePallet? Pallet { get; }
    public TraceCarton? Carton { get; }
    public TraceProduct? Product { get; }

    public static TraceResultViewModel From(TraceabilityResult result, LanguageService language)
        => new(result, result.MatchedType switch
        {
            ScanCodeType.Product => language.Get("Trace.Type.Product"),
            ScanCodeType.Carton => language.Get("Trace.Type.Carton"),
            ScanCodeType.Pallet => language.Get("Trace.Type.Pallet"),
            _ => result.MatchedType.ToString()
        });
}
