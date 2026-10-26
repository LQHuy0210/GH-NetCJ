using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GH_Net.Core.Enums;
using GH_Net.Licensing;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Data;
using GH_Net.Repository.Devices;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using GH_Net.Service.Implements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var decoder = new CameraLineFrameDecoder(16);
Check.That(decoder.Append(Encoding.UTF8.GetBytes(" P-1 \r")).Single() == " P-1 ", "Exact camera codes must preserve spaces.");
Check.That(decoder.Append(Encoding.UTF8.GetBytes("\nA\nB\r\n")).SequenceEqual(["A", "B"]), "Split/coalesced framing failed.");
byte[] unicode = Encoding.UTF8.GetBytes("SẢN\r");
Check.That(decoder.Append(unicode.AsSpan(0, 2)).Count == 0, "Partial UTF-8 emitted early.");
Check.That(decoder.Append(unicode.AsSpan(2)).Single() == "SẢN", "Split UTF-8 failed.");
Check.Throws<InvalidDataException>(() => decoder.Append(Encoding.UTF8.GetBytes(new string('x', 17))));
Check.Throws<DecoderFallbackException>(() => decoder.Append([255]));
Check.That(PlcCommandCodec.Synchronize(123) == "00000123", "ZRP synchronization differs.");
Check.That(PlcCommandCodec.Reject(PlcRejectMode.FixedCommand, 1) == "REJECT", "ZRP fixed reject differs.");
Check.That(PlcCommandCodec.Reject(PlcRejectMode.IndexedCommand, 123) == PlcCommandCodec.DlmErrorCommand + "0000123", "ZRP indexed reject differs.");
Check.Throws<ArgumentOutOfRangeException>(() => PlcCommandCodec.Reject(PlcRejectMode.IndexedCommand, 0));
Check.Throws<ArgumentOutOfRangeException>(() => PlcCommandCodec.Synchronize(10_000_000));
Check.Throws<InvalidOperationException>(() => GH_Net.Core.Packing.PackingPolicy.ValidateMode(PackingMode.SemiAutomatic, false));
Check.Throws<InvalidOperationException>(() => GH_Net.Core.Packing.PackingPolicy.ValidateMode((PackingMode)99, true));

var strict = new CameraLineFrameDecoder(); strict.SetTerminator("CRLF");
Check.That(strict.Append(Encoding.UTF8.GetBytes("A\r")).Count == 0, "CRLF selection must buffer a partial terminator.");
Check.That(strict.Append(Encoding.UTF8.GetBytes("\n")).Single() == "A", "CRLF selection did not complete a frame.");
strict.SetTerminator("LF"); Check.That(strict.Append(Encoding.UTF8.GetBytes("B\r\n")).Single() == "B", "LF framing must strip CRLF transport characters.");
foreach (PlcFraming framing in Enum.GetValues<PlcFraming>())
foreach (PackingMode mode in Enum.GetValues<PackingMode>())
{
    await using var f = await Fixture.CreateAsync(mode, framing, PlcRejectMode.IndexedCommand);
    Check.That(f.PlcMessages.IsEmpty, "Apply must not actuate a PLC.");
    await f.StartAsync();
    await Check.UntilAsync(() => f.PlcMessages.Contains("READY"));
    string Wire(string command) => framing switch
    {
        PlcFraming.CR => command + "\r", PlcFraming.LF => command + "\n",
        PlcFraming.CRLF => command + "\r\n", _ => "\u0002" + command + "\u0003"
    };
    string initial = Wire("HOLD") + (mode == PackingMode.SemiAutomatic ? Wire("00000000") : "") + Wire("READY");
    Check.That(f.PlcBytes.ToArray().SequenceEqual(Encoding.ASCII.GetBytes(initial)), "PLC wire framing/order differs from ZRP/CJ contract.");
    if (mode == PackingMode.SemiAutomatic)
    {
        Check.That(f.PlcMessages.Contains("00000000"), "Start must synchronize ZRP Index before product admission.");
        await f.SendCameraAsync("NOREAD\r\n");
        await f.WaitSequenceAsync(1);
        Check.That(f.Runtime.Current?.CheckedProducts == 0, "No-read must not aggregate a Product.");
        await Check.UntilAsync(() => f.PlcMessages.Contains(PlcCommandCodec.Reject(PlcRejectMode.IndexedCommand, 1)));
        await f.SendCameraAsync(" P-1 \r\n");
        await f.WaitSequenceAsync(2);
        Check.That(f.Runtime.Current?.CheckedProducts == 1, "Camera product was not accepted.");
        await f.SendCameraAsync(" P-1 \r\n");
        await f.WaitSequenceAsync(3);
        Check.That(f.Runtime.Current?.LastResult == ScanResultCode.Duplicate, "Camera duplicate was not rejected.");
        await f.Runtime.HandleScanAsync("P-2");
        Check.That(f.Runtime.Current?.LastResult == ScanResultCode.WrongCodeType, "Scanner must not bypass camera product inspection.");
        await f.PlcPeer.GetStream().WriteAsync(Encoding.ASCII.GetBytes("\u0002PLC001\u0003"));
        await Check.UntilAsync(() => f.PlcMessages.Contains("00000003"));
        await f.SendCameraAsync("P-2\r");
        await f.WaitSequenceAsync(4);
    }
    else
    {
        await f.SendCameraAsync("P-2\r");
        await Task.Delay(80);
        Check.That(f.Runtime.Current?.CheckedProducts == 0, "Manual Job must ignore the camera.");
        await f.Runtime.HandleScanAsync(" P-1 ");
        await f.Runtime.HandleScanAsync("P-2");
    }
    Check.That(f.Printer.Labels.Count == 1 && f.Runtime.Current?.WorkflowState == PackingWorkflowState.CartonWaitingConfirmation,
        "A full carton must print once and await confirmation.");
    await Check.UntilAsync(() => f.PlcMessages.Contains("FULL"));
    await Check.ThrowsAsync<InvalidOperationException>(() => f.Automation.ApplyAsync(f.Config));
    await f.Runtime.HandleScanAsync("C-1");
    Check.That(f.Printer.Labels.Count == 2, "A full pallet must print once.");
    await f.Runtime.HandleScanAsync("L-1");
    Check.That(!f.Runtime.IsRunning && f.Runtime.Current?.WorkflowState == PackingWorkflowState.Completed, "Final pallet must complete the Job.");
    await Check.UntilAsync(() => f.PlcMessages.Contains("STOP"));
    using var uow = f.Factory.Create();
    var scans = await uow.Repository<ScanEvent>().Query().ToListAsync();
    Check.That(mode != PackingMode.SemiAutomatic || scans.Count(x => x.Source == "Camera") == 4, "Camera audit is incomplete.");
    Check.That(await uow.Repository<LineEvent>().Query().AllAsync(x => x.Outcome == "SentToSocket"), "PLC socket outcomes missing.");
    using var output = new MemoryStream();
    await new PackingExportService(f.Factory).ExportAsync(1, new(PackingExportFormat.CsvZip), output);
    output.Position = 0; using var zip = new ZipArchive(output, ZipArchiveMode.Read);
    Check.That(zip.GetEntry("LineEvents.csv") is not null, "PLC report missing.");
    Console.WriteLine($"Automation passed: {mode}, {framing}, Index, full Product–Carton–Pallet flow.");
}

await using (var f = await Fixture.CreateAsync(PackingMode.SemiAutomatic, PlcFraming.CRLF, PlcRejectMode.FixedCommand))
{
    await f.StartAsync();
    await f.SendCameraAsync("INVALID\r\n"); await f.WaitSequenceAsync(1);
    await Check.UntilAsync(() => f.PlcMessages.Contains("REJECT"));
    f.PlcPeer.Dispose();
    await Check.UntilAsync(() => !f.Runtime.IsRunning);
    using var uow = f.Factory.Create();
    var job = await uow.Repository<Job>().FindAsync(1);
    Check.That(job?.Status == JobStatus.Paused && job.LastCameraSequence == 1, "PLC loss must pause and preserve sequence.");
    Check.That(f.Runtime.Current?.CheckedProducts == 0, "An invalid Product was aggregated.");
}

await using (var f = await Fixture.CreateAsync(PackingMode.SemiAutomatic, PlcFraming.CRLF, PlcRejectMode.IndexedCommand))
{
    await f.StartAsync();
    await f.SendCameraAsync(" P-1 \r\n"); await f.WaitSequenceAsync(1);
    await f.SendCameraAsync("STALE-PARTIAL");
    await Task.Delay(40);
    await f.Runtime.StopAsync();
    await f.StartAsync();
    await Check.UntilAsync(() => f.PlcMessages.Contains("00000001"));
    Check.That(f.Runtime.Current?.CheckedProducts == 1 && f.Printer.Labels.Count == 0, "Resume must retain associations without printing.");
    await f.SendCameraAsync("P-2\r\n"); await f.WaitSequenceAsync(2);
    Check.That(f.Runtime.Current?.CheckedProducts == 2, "Partial data from the stopped stream leaked into the new session.");
    f.CameraPeer.Dispose();
    await Check.UntilAsync(() => !f.Runtime.IsRunning);
}

await using (var f = await Fixture.CreateAsync(PackingMode.SemiAutomatic, PlcFraming.CRLF, PlcRejectMode.FixedCommand))
{
    f.Printer.Block = true;
    await f.StartAsync();
    await f.SendCameraAsync(" P-1 \r\n"); await f.WaitSequenceAsync(1);
    await f.SendCameraAsync("P-2\r\n");
    await f.Printer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await f.SendCameraAsync("EXCESS\r\nANOTHER\r\n");
    await Task.Delay(80); f.Printer.Release.TrySetResult();
    await Check.UntilAsync(() => !f.Runtime.IsRunning);
    using var uow = f.Factory.Create();
    Check.That(await uow.Repository<ScanEvent>().Query().AnyAsync(x => x.Source == "Camera" && x.ResultCode == ScanResultCode.Busy), "Camera overrun audit missing.");
    Check.That(await uow.Repository<Product>().Query().CountAsync(x => x.Status != ProductStatus.Available) == 2, "Overrun mutated product packing.");
}

await using (var f = await Fixture.CreateAsync(PackingMode.SemiAutomatic, PlcFraming.CRLF, PlcRejectMode.IndexedCommand))
{
    f.License.Features = LicenseFeature.Scanner | LicenseFeature.Printer;
    await Check.ThrowsAsync<InvalidOperationException>(() => f.Runtime.StartAsync(1));
    Check.That(!f.Runtime.IsRunning && f.PlcMessages.IsEmpty, "Denied automation license emitted PLC commands.");
    f.License.Features = LicenseFeature.All;
    using (var uow = f.Factory.Create())
    {
        uow.Repository<LineEvent>().Add(new() { JobId = 1, Kind = "Reject", Payload = "REJECT" });
        await uow.SaveChangesAsync();
    }
    using (var recovery = f.Factory.Create()) await new JobService(recovery).RecoverCrashedJobsAsync();
    using (var verify = f.Factory.Create())
        Check.That(await verify.Repository<LineEvent>().Query().AllAsync(x => x.Outcome == "Unknown"), "Crash recovery must mark pending sends uncertain.");
    await f.StartAsync();
    Check.That(!f.PlcMessages.Contains("REJECT"), "Recovery replayed an uncertain reject.");
}
Console.WriteLine("Automation smoke passed: ZRP transports, modes, licenses, framing, source isolation, rejection, recovery, no replay, overrun, audit/export.");

await using (var f = await Fixture.CreateAsync(PackingMode.Manual, PlcFraming.CRLF, PlcRejectMode.FixedCommand))
{
    var saved = await f.Automation.GetAsync();
    f.PlcPeer.Dispose();
    await Check.UntilAsync(() => !f.Automation.PlcConnected);
    var cameraDraft = await f.Automation.GetAsync();
    cameraDraft.NoReadToken = "UNREAD";
    cameraDraft.PlcIp = "unapplied-invalid-plc";
    cameraDraft.StopCommand = "UNAPPLIED";
    f.License.Features = LicenseFeature.Camera;
    await f.Automation.ApplyCameraAsync(cameraDraft);
    var afterCamera = await f.Automation.GetAsync();
    Check.That(afterCamera.NoReadToken == "UNREAD" && afterCamera.PlcIp == saved.PlcIp && afterCamera.StopCommand == saved.StopCommand,
        "Camera Apply must preserve saved PLC settings and ignore its unapplied draft.");
    Check.That(f.Automation.CameraConnected && !f.Automation.PlcConnected, "Camera Apply must not connect the offline PLC.");
    await Check.ThrowsAsync<InvalidOperationException>(() => f.Automation.ApplyPlcAsync(cameraDraft));

    using var replacementPlc = new TcpListener(IPAddress.Loopback, 0); replacementPlc.Start();
    var plcDraft = await f.Automation.GetAsync();
    plcDraft.PlcPort = ((IPEndPoint)replacementPlc.LocalEndpoint).Port;
    plcDraft.HoldCommand = "WAIT";
    plcDraft.Sensors[0].DelayDistance = 18;
    plcDraft.Sensors[1].PulseCount = 7200;
    plcDraft.CameraIp = "unapplied-invalid-camera";
    plcDraft.NoReadToken = "UNAPPLIED";
    f.License.Features = LicenseFeature.Plc;
    await f.Automation.ApplyPlcAsync(plcDraft);
    using var replacementPeer = await replacementPlc.AcceptTcpClientAsync();
    var afterPlc = await f.Automation.GetAsync();
    Check.That(afterPlc.HoldCommand == "WAIT" && afterPlc.CameraIp == saved.CameraIp && afterPlc.NoReadToken == "UNREAD" &&
        afterPlc.Sensors[0].DelayDistance == 18 && afterPlc.Sensors[1].PulseCount == 7200,
        "PLC Apply must persist sensor settings and preserve camera fields.");
    Check.That(f.Automation.CameraConnected && f.Automation.PlcConnected, "PLC Apply must connect only the PLC.");
    Check.That(replacementPeer.Available == 0, "PLC Apply must only connect; no sensor or actuation command is permitted.");
    f.Access.Lock();
    plcDraft.HoldCommand = "UNAUTHORIZED"; plcDraft.Framing = PlcFraming.STX_ETX; plcDraft.Sensors[0].DelayDistance = 999;
    await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Automation.ApplyPlcAsync(plcDraft));
    await f.Automation.ApplyPlcConnectionAsync(plcDraft);
    var lockedSave = await f.Automation.GetAsync();
    Check.That(lockedSave.HoldCommand == "WAIT" && lockedSave.Framing == afterPlc.Framing && lockedSave.Sensors[0].DelayDistance == 18,
        "Operator connection-only Apply modified protected protocol or sensor fields.");
    await Check.ThrowsAsync<InvalidOperationException>(() => f.Automation.ApplyCameraAsync(plcDraft));
}
await using (var f = await Fixture.CreateAsync(PackingMode.SemiAutomatic, PlcFraming.CRLF, PlcRejectMode.FixedCommand))
{
    await f.StartAsync();
    await f.SendCameraAsync(" P-1 \r\n"); await f.WaitSequenceAsync(1);
    await f.SendCameraAsync(" P-1 \r\n"); await f.WaitSequenceAsync(2);
    await f.SendCameraAsync("NOREAD\r\n"); await f.WaitSequenceAsync(3);
    await f.SendCameraAsync("INVALID\r\n"); await f.WaitSequenceAsync(4);
    using var uow = f.Factory.Create();
    var signals = await uow.Repository<LineEvent>().Query().Where(x => x.Kind == "Reject").OrderBy(x => x.Id).ToListAsync();
    Check.That(signals.Count == 3 && signals.All(x => x.Payload == "REJECT" && x.Outcome == "SentToSocket"),
        "CJ must request rejection for every duplicate, no-read and invalid camera result.");
    Check.That(f.Runtime.Current?.CheckedProducts == 1, "Invalid/duplicate camera results must not aggregate products.");
}
Console.WriteLine("Independent camera/PLC settings passed: partial saves, device licenses, offline isolation and no actuation.");

static class Check
{
    public static void That(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
    public static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(8000);
        while (!predicate()) await Task.Delay(15, timeout.Token);
    }
}

sealed class Fixture : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ghnet-automation-{Guid.NewGuid():N}.db");
    private readonly TcpListener _cameraServer = new(IPAddress.Loopback, 0), _plcServer = new(IPAddress.Loopback, 0);
    private readonly CameraDevice _camera = new();
    private readonly PlcDevice _plc = new();
    private Task? _capture;
    public TcpClient CameraPeer { get; private set; } = null!;
    public TcpClient PlcPeer { get; private set; } = null!;
    public ConcurrentQueue<string> PlcMessages { get; } = new();
    public ConcurrentQueue<byte> PlcBytes { get; } = new();
    public UnitOfWorkFactory Factory { get; private set; } = null!;
    public AutomationConfig Config { get; private set; } = null!;
    public AutomationService Automation { get; private set; } = null!;
    public PackingRuntimeService Runtime { get; private set; } = null!;
    public TestPrinter Printer { get; } = new();
    public TestLicense License { get; } = new();
    public TestTechnicalAccess Access { get; } = new();
    public static async Task<Fixture> CreateAsync(PackingMode mode, PlcFraming framing, PlcRejectMode rejectMode)
    {
        var f = new Fixture();
        try
        {
            f._cameraServer.Start(); f._plcServer.Start();
            f.Factory = new($"Data Source={f._path}");
            await using (var db = PrinterManagerDbContext.Create($"Data Source={f._path}"))
            {
                await db.Database.MigrateAsync();
                db.Jobs.Add(new() { Name = "Automation", Mode = mode, UsePlc = true, ProductsPerCarton = 2, CartonsPerPallet = 1, TotalData = 2 });
                await db.SaveChangesAsync();
                db.Products.AddRange(new Product { JobId = 1, Code = " P-1 ", SourceRowIndex = 0 }, new Product { JobId = 1, Code = "P-2", SourceRowIndex = 1 });
                db.Cartons.Add(new() { JobId = 1, Code = "C-1", SourceRowIndex = 0 }); db.Pallets.Add(new() { JobId = 1, Code = "L-1", SourceRowIndex = 0 });
                await db.SaveChangesAsync();
            }
            f.Config = new() { CameraPort = ((IPEndPoint)f._cameraServer.LocalEndpoint).Port, PlcPort = ((IPEndPoint)f._plcServer.LocalEndpoint).Port, Framing = framing, RejectMode = rejectMode, TimeoutMs = 1000 };
            f.Automation = new(f.Factory, f._camera, f._plc, f.License, f.Access);
            await f.Automation.ApplyAsync(f.Config);
            f.CameraPeer = await f._cameraServer.AcceptTcpClientAsync(); f.PlcPeer = await f._plcServer.AcceptTcpClientAsync();
            f._capture = Task.Run(async () =>
            {
                var decoder = new PlcMessageDecoder(); byte[] buffer = new byte[4096];
                try { while (true) { int count = await f.PlcPeer.GetStream().ReadAsync(buffer); if (count == 0) break; for (int i = 0; i < count; i++) f.PlcBytes.Enqueue(buffer[i]); foreach (var frame in decoder.Feed(buffer.AsSpan(0, count))) f.PlcMessages.Enqueue(frame); } }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
            });
            f.Runtime = new(f.Factory, new TestScanner(), f.Printer, new TestPrinterConfig(), new ZplTemplateRenderer(), f.Automation);
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }
    public async Task StartAsync()
    {
        using var uow = Factory.Create();
        var job = await uow.Repository<Job>().FindAsync(1);
        if (job?.Mode != PackingMode.SemiAutomatic) { await Runtime.StartAsync(1); return; }
        var accept = _cameraServer.AcceptTcpClientAsync();
        await Runtime.StartAsync(1);
        CameraPeer.Dispose();
        CameraPeer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public Task SendCameraAsync(string frame) => CameraPeer.GetStream().WriteAsync(Encoding.UTF8.GetBytes(frame)).AsTask();
    public async Task WaitSequenceAsync(long sequence)
    {
        using var timeout = new CancellationTokenSource(8000);
        while (true)
        {
            using var uow = Factory.Create();
            if (!Runtime.IsBusy && await uow.Repository<ScanEvent>().Query().AnyAsync(x => x.CameraSequence == sequence, timeout.Token)) return;
            await Task.Delay(15, timeout.Token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        Printer.Release.TrySetResult();
        if (Runtime is not null) { try { await Runtime.StopAsync(); } catch { } Runtime.Dispose(); }
        Automation?.Dispose();
        await _camera.DisposeAsync(); await _plc.DisposeAsync();
        CameraPeer?.Dispose(); PlcPeer?.Dispose(); _cameraServer.Stop(); _plcServer.Stop();
        if (_capture is not null) await _capture;
        SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path);
    }
}
sealed class TestLicense : ILicenseManager
{
    public LicenseFeature Features { get; set; } = LicenseFeature.All;
    public LicenseSnapshot Current => new() { Status = LicenseValidationStatus.Valid, HardwareId = "SMOKE", Features = Features };
    public event EventHandler? LicenseChanged;
    public bool HasFeature(LicenseFeature feature) => (Features & feature) == feature;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<LicenseValidationResult> ActivateAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class TestScanner : IScannerDevice
{
    public bool IsConnected => true; public string? CurrentPortName => "COM1";
    public event EventHandler<ScannerCodeEventArgs>? CodeReceived; public event EventHandler? ConnectionStateChanged; public event EventHandler<Exception>? ReadFailed;
    public IReadOnlyList<string> GetAvailablePortNames() => ["COM1"];
    public Task<bool> ConnectAsync(ScannerConfig config, CancellationToken ct = default) => Task.FromResult(true);
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class TestPrinter : IZebraPrinterDevice
{
    public List<string> Labels { get; } = [];
    public bool Block { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsConnected => true; public string? CurrentIp => "127.0.0.1"; public int CurrentPort => 9100;
    public event EventHandler? ConnectionStateChanged;
    public Task<bool> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(true);
    public async Task SendZplAsync(string zpl, TimeSpan timeout, CancellationToken ct = default) { Labels.Add(zpl); if (Block) { Started.TrySetResult(); await Release.Task.WaitAsync(ct); } }
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class TestPrinterConfig : IZebraPrinterConfigService
{
    public Task<PrinterConfig?> GetAsync(CancellationToken ct = default) => Task.FromResult<PrinterConfig?>(new() { Ip = "127.0.0.1", Port = 9100, CartonZplTemplate = "^XA^FD{{CODE}}^FS^XZ", PalletZplTemplate = "^XA^FD{{CODE}}^FS^XZ" });
    public Task<ZebraPrinterApplyResult> ApplyAsync(PrinterConfig config, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ConnectSavedAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task TestPrintAsync(bool carton, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class TestTechnicalAccess : ITechnicalSettingsAccess
{
    public bool IsUnlocked { get; private set; } = true;
    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task ConfigureAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TechnicalUnlockResult> UnlockAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    public void Lock() => IsUnlocked = false;
    public void EnsureUnlocked() { if (!IsUnlocked) throw new UnauthorizedAccessException(); }
}
