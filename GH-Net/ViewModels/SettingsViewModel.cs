using GH_Net.Core.Enums;
using GH_Net.Licensing;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Collections.ObjectModel;

namespace GH_Net.WPF.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private const string DefaultCartonZpl = "^XA^FO40,40^BY2^BCN,100,Y,N,N^FD{{CODE}}^FS^XZ";
    private const string DefaultPalletZpl = "^XA^FO40,40^BY3^BCN,140,Y,N,N^FD{{CODE}}^FS^XZ";
    private readonly IJobService _jobs;
    private readonly IZebraPrinterConfigService _printer;
    private readonly IScannerConfigService _scanner;
    private readonly ILicenseManager _license;
    private readonly LanguageService _language;
    private bool _isBusy;
    private bool _isLocked;
    private bool _disposed;
    private string _printerIp = string.Empty;
    private string _printerPort = "9100";
    private string _cartonZplTemplate = DefaultCartonZpl;
    private string _palletZplTemplate = DefaultPalletZpl;
    private string _printerStatusMessage = string.Empty;
    private string? _selectedScannerPort;
    private int _selectedBaudRate = 9600;
    private int _selectedDataBits = 8;
    private int _selectedParity;
    private int _selectedStopBits = 1;
    private string _scannerTerminator = "CR";
    private string _scannerTimeout = "3000";
    private string _scannerStatusMessage = string.Empty;

    public SettingsViewModel(
        IJobService jobs,
        IZebraPrinterConfigService printer,
        IScannerConfigService scanner,
        ILicenseManager license,
        LanguageService language)
    {
        _jobs = jobs; _printer = printer; _scanner = scanner; _license = license; _language = language;
        ApplyPrinterCommand = new AsyncRelayCommand(ApplyPrinterAsync, CanEditPrinter);
        TestCartonPrintCommand = new AsyncRelayCommand(() => TestPrintAsync(true), CanEditPrinter);
        TestPalletPrintCommand = new AsyncRelayCommand(() => TestPrintAsync(false), CanEditPrinter);
        ApplyScannerCommand = new AsyncRelayCommand(ApplyScannerAsync, CanEditScanner);
        RefreshScannerPortsCommand = new RelayCommand<object>(_ => RefreshScannerPorts(), _ => CanEditScanner());
        _language.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<string> ScannerPorts { get; } = [];
    public IReadOnlyList<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200];
    public IReadOnlyList<int> DataBitsOptions { get; } = [7, 8];
    public IReadOnlyList<int> ParityOptions { get; } = [0, 1, 2, 3, 4];
    public IReadOnlyList<int> StopBitsOptions { get; } = [1, 2, 3];
    public IReadOnlyList<string> ScannerTerminatorOptions { get; } = ["CR", "LF", "CRLF"];
    public AsyncRelayCommand ApplyPrinterCommand { get; }
    public AsyncRelayCommand TestCartonPrintCommand { get; }
    public AsyncRelayCommand TestPalletPrintCommand { get; }
    public AsyncRelayCommand ApplyScannerCommand { get; }
    public RelayCommand<object> RefreshScannerPortsCommand { get; }
    public bool CanUsePrinter => _license.HasFeature(LicenseFeature.Printer);
    public bool CanUseScanner => _license.HasFeature(LicenseFeature.Scanner);

    public string PrinterIp { get => _printerIp; set => SetProperty(ref _printerIp, value); }
    public string PrinterPort { get => _printerPort; set => SetProperty(ref _printerPort, value); }
    public string CartonZplTemplate { get => _cartonZplTemplate; set => SetProperty(ref _cartonZplTemplate, value); }
    public string PalletZplTemplate { get => _palletZplTemplate; set => SetProperty(ref _palletZplTemplate, value); }
    public string PrinterStatusMessage { get => _printerStatusMessage; private set => SetProperty(ref _printerStatusMessage, value); }
    public string? SelectedScannerPort { get => _selectedScannerPort; set => SetProperty(ref _selectedScannerPort, value); }
    public int SelectedBaudRate { get => _selectedBaudRate; set => SetProperty(ref _selectedBaudRate, value); }
    public int SelectedDataBits { get => _selectedDataBits; set => SetProperty(ref _selectedDataBits, value); }
    public int SelectedParity { get => _selectedParity; set => SetProperty(ref _selectedParity, value); }
    public int SelectedStopBits { get => _selectedStopBits; set => SetProperty(ref _selectedStopBits, value); }
    public string ScannerTerminator { get => _scannerTerminator; set => SetProperty(ref _scannerTerminator, value); }
    public string ScannerTimeout { get => _scannerTimeout; set => SetProperty(ref _scannerTimeout, value); }
    public string ScannerStatusMessage { get => _scannerStatusMessage; private set => SetProperty(ref _scannerStatusMessage, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); } }
    public bool IsLocked { get => _isLocked; private set { if (SetProperty(ref _isLocked, value)) RefreshCommands(); } }
    public bool CanEdit => !IsBusy && !IsLocked;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            IsLocked = (await _jobs.GetAllAsync()).Any(job => job.Status == JobStatus.Running);
            PrinterConfig? printer = await _printer.GetAsync();
            PrinterIp = printer?.Ip ?? string.Empty;
            PrinterPort = (printer?.Port ?? 9100).ToString();
            CartonZplTemplate = printer?.CartonZplTemplate ?? DefaultCartonZpl;
            PalletZplTemplate = printer?.PalletZplTemplate ?? DefaultPalletZpl;
            PrinterStatusMessage = printer is null ? _language.Get("Settings.ZebraNotConfigured") : _language.Get("Settings.ZebraLoaded");
            RefreshScannerPorts();
            ScannerConfig? scanner = await _scanner.GetAsync();
            if (scanner is not null)
            {
                if (!ScannerPorts.Contains(scanner.PortName)) ScannerPorts.Add(scanner.PortName);
                SelectedScannerPort = scanner.PortName; SelectedBaudRate = scanner.BaudRate;
                SelectedDataBits = scanner.DataBits; SelectedParity = scanner.Parity;
                SelectedStopBits = scanner.StopBits; ScannerTerminator = scanner.MessageTerminator;
                ScannerTimeout = scanner.ReadTimeoutMs.ToString();
            }
            ScannerStatusMessage = scanner is null ? _language.Get("Settings.ScannerNotConfigured") : _language.Get("Settings.ScannerLoaded");
        }
        catch (Exception exception) { PrinterStatusMessage = ScannerStatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private bool CanEditSettings() => !IsBusy && !IsLocked;
    private bool CanEditPrinter() => CanEditSettings() && CanUsePrinter;
    private bool CanEditScanner() => CanEditSettings() && CanUseScanner;

    private async Task ApplyPrinterAsync()
    {
        if (!int.TryParse(PrinterPort, out int port)) { PrinterStatusMessage = _language.Get("Settings.InvalidPort"); return; }
        IsBusy = true;
        try
        {
            ZebraPrinterApplyResult result = await _printer.ApplyAsync(new PrinterConfig
            {
                Ip = PrinterIp.Trim(), Port = port,
                CartonZplTemplate = CartonZplTemplate, PalletZplTemplate = PalletZplTemplate,
                ConnectTimeoutMs = 3000, SendTimeoutMs = 3000
            });
            PrinterStatusMessage = result.Connected ? _language.Get("Settings.ZebraConnected") : _language.Get("Settings.ZebraDisconnected");
        }
        catch (Exception exception) { PrinterStatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private async Task TestPrintAsync(bool carton)
    {
        IsBusy = true;
        try { await _printer.TestPrintAsync(carton); PrinterStatusMessage = _language.Get("Settings.TestPrintSent"); }
        catch (Exception exception) { PrinterStatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private async Task ApplyScannerAsync()
    {
        if (!int.TryParse(ScannerTimeout, out int timeout)) { ScannerStatusMessage = _language.Get("Settings.ScannerInvalidTimeout"); return; }
        IsBusy = true;
        try
        {
            ScannerApplyResult result = await _scanner.ApplyAsync(new ScannerConfig
            {
                PortName = SelectedScannerPort ?? string.Empty, BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits, Parity = SelectedParity, StopBits = SelectedStopBits,
                MessageTerminator = ScannerTerminator, ReadTimeoutMs = timeout, Enabled = true
            });
            ScannerStatusMessage = result.Connected ? _language.Get("Settings.ScannerConnected") : _language.Get("Settings.ScannerDisconnected");
        }
        catch (Exception exception) { ScannerStatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private void RefreshScannerPorts()
    {
        string? selected = SelectedScannerPort;
        ScannerPorts.Clear();
        foreach (string port in _scanner.GetAvailablePortNames()) ScannerPorts.Add(port);
        if (selected is not null && !ScannerPorts.Contains(selected)) ScannerPorts.Add(selected);
        SelectedScannerPort = selected ?? ScannerPorts.FirstOrDefault();
    }

    private void RefreshCommands()
    {
        ApplyPrinterCommand.NotifyCanExecuteChanged(); TestCartonPrintCommand.NotifyCanExecuteChanged();
        TestPalletPrintCommand.NotifyCanExecuteChanged(); ApplyScannerCommand.NotifyCanExecuteChanged();
        RefreshScannerPortsCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) { }
    public void Dispose() { if (_disposed) return; _disposed = true; _language.LanguageChanged -= OnLanguageChanged; }
}
