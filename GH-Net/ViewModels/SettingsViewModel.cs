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
    private readonly IAutomationService? _automation;
    private readonly ITechnicalSettingsAccess? _technicalAccess;
    private bool _isTechnicalUnlocked;
    private AutomationConfig _automationConfig = new();
    private string _cameraMessage = string.Empty;
    private string _plcMessage = string.Empty;
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
        LanguageService language, IAutomationService? automation = null, ITechnicalSettingsAccess? technicalAccess = null)
    {
        _jobs = jobs; _printer = printer; _scanner = scanner; _license = license; _language = language;
        _automation = automation;
        _technicalAccess = technicalAccess;
        PlcSensors = [new PlcSensorSettingsViewModel(language, 1), new PlcSensorSettingsViewModel(language, 2)];
        ApplyCameraCommand = new AsyncRelayCommand(() => ApplyDeviceAsync(true), () => CanEdit && CanUseCamera);
        ApplyPlcCommand = new AsyncRelayCommand(() => ApplyDeviceAsync(false), () => CanEdit && CanUsePlc);
        if (_automation is not null) _automation.ConnectionStateChanged += OnAutomationState;
        ApplyPrinterCommand = new AsyncRelayCommand(ApplyPrinterAsync, CanEditPrinter);
        TestCartonPrintCommand = new AsyncRelayCommand(() => TestPrintAsync(true), CanEditPrinter);
        TestPalletPrintCommand = new AsyncRelayCommand(() => TestPrintAsync(false), CanEditPrinter);
        ApplyScannerCommand = new AsyncRelayCommand(ApplyScannerAsync, CanEditScanner);
        RefreshScannerPortsCommand = new RelayCommand<object>(_ => RefreshScannerPorts(), _ => CanEditScanner());
        _language.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<string> ScannerPorts { get; } = [];
    public IReadOnlyList<PlcSensorSettingsViewModel> PlcSensors { get; }
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
    public bool CanUseCamera => _license.HasFeature(LicenseFeature.Camera);
    public bool CanUsePlc => _license.HasFeature(LicenseFeature.Plc);
    public AsyncRelayCommand ApplyCameraCommand { get; }
    public AsyncRelayCommand ApplyPlcCommand { get; }
    public bool IsTechnicalUnlocked { get => _isTechnicalUnlocked; private set => SetProperty(ref _isTechnicalUnlocked, value); }
    public string PlcSynchronizationRequest => GH_Net.Repository.Devices.PlcCommandCodec.SynchronizationRequest;
    public void RefreshTechnicalAccess() => IsTechnicalUnlocked = _technicalAccess?.IsUnlocked == true;
    public void LockTechnicalSettings() { _technicalAccess?.Lock(); RefreshTechnicalAccess(); }
    public async Task ReloadTechnicalSettingsAsync()
    {
        if (_technicalAccess?.IsUnlocked != true || _automation is null) return;
        var saved = await _automation.GetAsync();
        Automation.Framing = saved.Framing; Automation.RejectMode = saved.RejectMode;
        Automation.TimeoutMs = saved.TimeoutMs; Automation.ReadyCommand = saved.ReadyCommand;
        Automation.HoldCommand = saved.HoldCommand; Automation.FullCommand = saved.FullCommand;
        Automation.FaultCommand = saved.FaultCommand; Automation.StopCommand = saved.StopCommand;
        LoadSensors(saved.Sensors);
        OnPropertyChanged(nameof(Automation)); OnPropertyChanged(nameof(SelectedPlcFraming));
        OnPropertyChanged(nameof(IsFixedPlcRejectMode)); OnPropertyChanged(nameof(IsIndexedPlcRejectMode));
        RefreshTechnicalAccess();
    }
    public AutomationConfig Automation { get => _automationConfig; private set => SetProperty(ref _automationConfig, value); }
    public IReadOnlyList<GH_Net.Core.Enums.PlcFraming> PlcFramings { get; } = Enum.GetValues<GH_Net.Core.Enums.PlcFraming>();
    public IReadOnlyList<GH_Net.Core.Enums.PlcRejectMode> PlcRejectModes { get; } = Enum.GetValues<GH_Net.Core.Enums.PlcRejectMode>();
    public IReadOnlyList<string> CameraTerminatorOptions { get; } = ["CR", "LF", "CRLF"];
    public string CameraIp { get => Automation.CameraIp; set { Automation.CameraIp = value; OnPropertyChanged(); } }
    public int CameraPort { get => Automation.CameraPort; set { Automation.CameraPort = value; OnPropertyChanged(); } }
    public string CameraTerminator { get => Automation.CameraTerminator; set { Automation.CameraTerminator = value; OnPropertyChanged(); } }
    public string PlcIp { get => Automation.PlcIp; set { Automation.PlcIp = value; OnPropertyChanged(); } }
    public int PlcPort { get => Automation.PlcPort; set { Automation.PlcPort = value; OnPropertyChanged(); } }
    public bool IsFixedPlcRejectMode { get => Automation.RejectMode == PlcRejectMode.FixedCommand; set { if (value) SetRejectMode(PlcRejectMode.FixedCommand); } }
    public bool IsIndexedPlcRejectMode { get => Automation.RejectMode == PlcRejectMode.IndexedCommand; set { if (value) SetRejectMode(PlcRejectMode.IndexedCommand); } }
    public IReadOnlyList<PlcFraming> PlcFramingOptions => PlcFramings;
    public PlcFraming SelectedPlcFraming { get => Automation.Framing; set { Automation.Framing = value; OnPropertyChanged(); } }
    private void SetRejectMode(PlcRejectMode mode)
    {
        Automation.RejectMode = mode;
        if (mode == PlcRejectMode.IndexedCommand) SelectedPlcFraming = PlcFraming.STX_ETX;
        OnPropertyChanged(nameof(IsFixedPlcRejectMode)); OnPropertyChanged(nameof(IsIndexedPlcRejectMode));
    }
    public string CameraStatusMessage => string.IsNullOrEmpty(CameraMessage) ? CameraConnectionStatus : CameraConnectionStatus + " · " + CameraMessage;
    public string PlcStatusMessage => string.IsNullOrEmpty(PlcMessage) ? PlcConnectionStatus : PlcConnectionStatus + " · " + PlcMessage;
    public string CameraStatusTone => CameraConnected ? "Success" : "Error";
    public string PlcStatusTone => PlcConnected ? "Success" : "Error";

    public string CameraMessage { get => _cameraMessage; private set => SetProperty(ref _cameraMessage, value); }
    public string PlcMessage { get => _plcMessage; private set => SetProperty(ref _plcMessage, value); }
    public bool CameraConnected => _automation?.CameraConnected == true;
    public bool PlcConnected => _automation?.PlcConnected == true;
    public string CameraConnectionStatus => _language.Get(CameraConnected ? "Common.Connected" : "Common.Disconnected");
    public string PlcConnectionStatus => _language.Get(PlcConnected ? "Common.Connected" : "Common.Disconnected");

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
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { RefreshCommands(); OnPropertyChanged(nameof(CanEdit)); } } }
    public bool IsLocked { get => _isLocked; private set { if (SetProperty(ref _isLocked, value)) { RefreshCommands(); OnPropertyChanged(nameof(CanEdit)); } } }
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
            if (_automation is not null) Automation = await _automation.GetAsync();
            LoadSensors(Automation.Sensors);
            foreach (string property in new[] { nameof(CameraIp), nameof(CameraPort), nameof(CameraTerminator), nameof(PlcIp), nameof(PlcPort), nameof(IsFixedPlcRejectMode), nameof(IsIndexedPlcRejectMode), nameof(SelectedPlcFraming) }) OnPropertyChanged(property);
            RefreshDeviceStatus();
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
                Ip = PrinterIp.Trim(),
                Port = port,
                CartonZplTemplate = CartonZplTemplate,
                PalletZplTemplate = PalletZplTemplate,
                ConnectTimeoutMs = 3000,
                SendTimeoutMs = 3000
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
                PortName = SelectedScannerPort ?? string.Empty,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                Parity = SelectedParity,
                StopBits = SelectedStopBits,
                MessageTerminator = ScannerTerminator,
                ReadTimeoutMs = timeout,
                Enabled = true
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
        ApplyCameraCommand.NotifyCanExecuteChanged();
        ApplyPlcCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyDeviceAsync(bool camera)
    {
        if (_automation is null) return;
        IsBusy = true;
        try
        {
            if (camera) { await _automation.ApplyCameraAsync(Automation); CameraMessage = string.Empty; }
            else
            {
                if (IsTechnicalUnlocked)
                {
                    if (PlcSensors.Any(sensor => !sensor.Validate()))
                    {
                        PlcMessage = string.Join(Environment.NewLine, PlcSensors.Where(sensor => sensor.Error.Length > 0).Select(sensor => sensor.Title + ": " + sensor.Error));
                        return;
                    }
                    Automation.Sensors = PlcSensors.Select(sensor => sensor.ToEntity()).ToList();
                    await _automation.ApplyPlcAsync(Automation);
                }
                else await _automation.ApplyPlcConnectionAsync(Automation);
                PlcMessage = string.Empty;
            }
        }
        catch (Exception ex) { if (camera) CameraMessage = ex.Message; else PlcMessage = ex.Message; }
        finally { IsBusy = false; RefreshDeviceStatus(); RefreshTechnicalAccess(); }
    }
    private void LoadSensors(IReadOnlyCollection<PlcSensorSettings> sensors)
    {
        var values = sensors.Count == 2 ? sensors : PlcSensorSettings.Defaults();
        foreach (var sensor in PlcSensors)
        {
            var saved = values.SingleOrDefault(value => value.Number == sensor.Number);
            sensor.Load(saved ?? PlcSensorSettings.Defaults().Single(value => value.Number == sensor.Number));
        }
    }
    private void RefreshDeviceStatus()
    {
        OnPropertyChanged(nameof(CameraConnectionStatus)); OnPropertyChanged(nameof(PlcConnectionStatus));
        OnPropertyChanged(nameof(CameraConnected)); OnPropertyChanged(nameof(PlcConnected));
        OnPropertyChanged(nameof(CameraStatusMessage)); OnPropertyChanged(nameof(PlcStatusMessage));
        OnPropertyChanged(nameof(CameraStatusTone)); OnPropertyChanged(nameof(PlcStatusTone));
    }
    private void OnAutomationState(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(RefreshDeviceStatus);
        else RefreshDeviceStatus();
    }
    private void OnLanguageChanged(object? sender, EventArgs e) { RefreshDeviceStatus(); foreach (var sensor in PlcSensors) sensor.RefreshLanguage(); }
    public void Dispose() { if (_disposed) return; _disposed = true; LockTechnicalSettings(); _language.LanguageChanged -= OnLanguageChanged; if (_automation is not null) _automation.ConnectionStateChanged -= OnAutomationState; }
}
