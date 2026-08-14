using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Data;
using GH_Net.Repository.Entities;
using GH_Net.Service.Implements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

string database = Path.Combine(Path.GetTempPath(), $"ghnet-device-config-{Guid.NewGuid():N}.db");
try
{
    await using var context = PrinterManagerDbContext.Create($"Data Source={database}");
    await context.Database.MigrateAsync();
    using var uow = new UnitOfWork(context);
    var scannerDevice = new FakeScannerDevice();
    var scannerService = new ScannerConfigService(uow, scannerDevice);
    var scannerResult = await scannerService.ApplyAsync(new ScannerConfig
    {
        PortName = "COM9", BaudRate = 115200, DataBits = 8, Parity = 0,
        StopBits = 1, MessageTerminator = "CRLF", ReadTimeoutMs = 2500
    });
    Assert(scannerResult.Connected && scannerDevice.Applied?.PortName == "COM9", "Scanner Apply did not persist/connect.");
    Assert((await scannerService.GetAsync())?.MessageTerminator == "CRLF", "Scanner configuration was not persisted.");

    var zebraDevice = new FakeZebraPrinterDevice();
    var zebraService = new ZebraPrinterConfigService(uow, zebraDevice, new ZplTemplateRenderer());
    var printerResult = await zebraService.ApplyAsync(new PrinterConfig
    {
        Ip = "127.0.0.1", Port = 9100,
        CartonZplTemplate = "^XA^FDC-{{CODE}}^FS^XZ",
        PalletZplTemplate = "^XA^FDP-{{CODE}}^FS^XZ",
        ConnectTimeoutMs = 2000, SendTimeoutMs = 2000
    });
    Assert(printerResult.Connected, "Zebra Apply did not connect.");
    await zebraService.TestPrintAsync(cartonTemplate: true);
    Assert(zebraDevice.LastZpl == "^XA^FDC-TEST-CARTON^FS^XZ", "Carton test print used the wrong template/code.");
    await zebraService.TestPrintAsync(cartonTemplate: false);
    Assert(zebraDevice.LastZpl == "^XA^FDP-TEST-PALLET^FS^XZ", "Pallet test print used the wrong template/code.");

    context.Jobs.Add(new Job { Name = "Running", Status = JobStatus.Running, ProductsPerCarton = 1, CartonsPerPallet = 1 });
    await context.SaveChangesAsync();
    await AssertThrowsAsync<InvalidOperationException>(() => scannerService.ApplyAsync(new ScannerConfig
    {
        PortName = "COM10", BaudRate = 9600, DataBits = 8, StopBits = 1,
        MessageTerminator = "CR", ReadTimeoutMs = 1000
    }));
    await AssertThrowsAsync<InvalidOperationException>(() => zebraService.TestPrintAsync(true));
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(database)) File.Delete(database);
}
Console.WriteLine("Device config smoke passed: Scanner/Zebra persistence/apply/test-print/running lock.");

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); } catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class FakeScannerDevice : IScannerDevice
{
    public bool IsConnected { get; private set; }
    public string? CurrentPortName => Applied?.PortName;
    public ScannerConfig? Applied { get; private set; }
    public event EventHandler<ScannerCodeEventArgs>? CodeReceived { add { } remove { } }
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler<Exception>? ReadFailed { add { } remove { } }
    public IReadOnlyList<string> GetAvailablePortNames() => ["COM9"];
    public Task<bool> ConnectAsync(ScannerConfig config, CancellationToken ct = default)
    { Applied = config; IsConnected = true; ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.FromResult(true); }
    public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class FakeZebraPrinterDevice : IZebraPrinterDevice
{
    public bool IsConnected { get; private set; }
    public string? CurrentIp { get; private set; }
    public int CurrentPort { get; private set; }
    public string? LastZpl { get; private set; }
    public event EventHandler? ConnectionStateChanged;
    public Task<bool> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct = default)
    { IsConnected = true; CurrentIp = ip; CurrentPort = port; ConnectionStateChanged?.Invoke(this, EventArgs.Empty); return Task.FromResult(true); }
    public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }
    public Task SendZplAsync(string zpl, TimeSpan timeout, CancellationToken ct = default) { LastZpl = zpl; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
