using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Data;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using GH_Net.Service.Implements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

string path = Path.Combine(Path.GetTempPath(), $"ghnet-packing-runtime-{Guid.NewGuid():N}.db");
try
{
    string cs = $"Data Source={path}"; await using (var db = PrinterManagerDbContext.Create(cs))
    {
        await db.Database.MigrateAsync();
        var job = new Job { Name = "Runtime smoke", Status = JobStatus.NewlyCreated, ProductsPerCarton = 2, CartonsPerPallet = 1, TotalData = 2 };
        db.Jobs.Add(job); await db.SaveChangesAsync();
        db.Products.AddRange(new Product { JobId = job.Id, Code = " P-1 ", SourceRowIndex = 1 }, new Product { JobId = job.Id, Code = "P-2", SourceRowIndex = 2 });
        db.Cartons.Add(new Carton { JobId = job.Id, Code = "C-1", SourceRowIndex = 1 }); db.Pallets.Add(new Pallet { JobId = job.Id, Code = "L-1", SourceRowIndex = 1 }); await db.SaveChangesAsync();
    }
    var factory = new UnitOfWorkFactory(cs); var printer = new FakePrinter();
    var runtime = new PackingRuntimeService(factory, new FakeScanner(), printer, new FakeConfig(), new ZplTemplateRenderer());
    await runtime.StartAsync(1);
    AssertStatistics(runtime.Current, 0, 2, 0, 1, 0, 1);
    await runtime.HandleScanAsync("P-1");
    Assert(runtime.Current?.LastResult == ScanResultCode.UnexpectedCode, "Product comparison must preserve leading and trailing whitespace.");
    await runtime.HandleScanAsync(" P-1 "); await runtime.HandleScanAsync(" P-1 ");
    AssertStatistics(runtime.Current, 1, 1, 0, 1, 0, 1);
    Assert(runtime.Current?.LastResult == ScanResultCode.Duplicate, "Duplicate scan must be rejected.");
    await runtime.HandleScanAsync("P-2"); Assert(printer.Labels.SequenceEqual(["C-1"]), "Full carton must print once.");
    AssertStatistics(runtime.Current, 2, 0, 0, 1, 0, 1);
    await runtime.HandleScanAsync("C-1"); Assert(printer.Labels.SequenceEqual(["C-1", "L-1"]), "Full pallet must print once.");
    AssertStatistics(runtime.Current, 2, 0, 1, 0, 0, 1);
    await runtime.HandleScanAsync("L-1");
    AssertStatistics(runtime.Current, 2, 0, 1, 0, 1, 0);
    await using var verify = PrinterManagerDbContext.Create(cs);
    Assert((await verify.Jobs.SingleAsync()).Status == JobStatus.Completed, "Job must complete after pallet confirmation.");
    Assert(await verify.Products.AllAsync(x => x.Status == ProductStatus.Packed), "Products must be packed atomically with carton confirmation.");
    Assert(await verify.PrintAttempts.CountAsync() == 2 && await verify.ScanEvents.CountAsync() == 6, "Audit trail must contain all prints and scans.");
    using var statisticsUow = factory.Create();
    PackingCodeStatistics persistedStatistics = await new JobService(statisticsUow).GetPackingCodeStatisticsAsync(1);
    Assert(persistedStatistics == new PackingCodeStatistics(2, 0, 1, 0, 1, 0),
        "Stopped/completed Job statistics must be loaded accurately from persisted statuses.");
    Console.WriteLine("Packing runtime smoke passed: exact-code/duplicate/Carton/Pallet/confirmation/completion.");
}
finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
await VerifyBusyScanIsRejectedAsync();
await VerifyPartialUnitsCannotPrintAsync();
static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

static void AssertStatistics(
    PackingRuntimeSnapshot? snapshot,
    int checkedProducts,
    int remainingProducts,
    int checkedCartons,
    int remainingCartons,
    int checkedPallets,
    int remainingPallets)
{
    if (snapshot is null) throw new InvalidOperationException("Runtime statistics snapshot is missing.");
    Assert(
        snapshot.CheckedProducts == checkedProducts
        && snapshot.RemainingProducts == remainingProducts
        && snapshot.CheckedCartons == checkedCartons
        && snapshot.RemainingCartons == remainingCartons
        && snapshot.CheckedPallets == checkedPallets
        && snapshot.RemainingPallets == remainingPallets,
        $"Unexpected code statistics: {snapshot}.");
}

static async Task VerifyPartialUnitsCannotPrintAsync()
{
    string path = Path.Combine(Path.GetTempPath(), $"ghnet-packing-full-only-{Guid.NewGuid():N}.db");
    try
    {
        string cs = $"Data Source={path}";
        await using (var db = PrinterManagerDbContext.Create(cs))
        {
            await db.Database.MigrateAsync();
            var cartonJob = new Job { Name = "Partial carton guard", ProductsPerCarton = 2, CartonsPerPallet = 1, TotalData = 2, PackingWorkflowState = PackingWorkflowState.CartonWaitingPrint };
            db.Jobs.Add(cartonJob); await db.SaveChangesAsync();
            db.Cartons.Add(new Carton { JobId = cartonJob.Id, Code = "C-PARTIAL", SourceRowIndex = 1, Status = CartonStatus.WaitingPrint, ProductCount = 1 });

            var palletJob = new Job { Name = "Partial pallet guard", ProductsPerCarton = 1, CartonsPerPallet = 2, TotalData = 2, PackingWorkflowState = PackingWorkflowState.PalletWaitingPrint };
            db.Jobs.Add(palletJob); await db.SaveChangesAsync();
            db.Pallets.Add(new Pallet { JobId = palletJob.Id, Code = "L-PARTIAL", SourceRowIndex = 1, Status = PalletStatus.WaitingPrint, CartonCount = 1 });
            await db.SaveChangesAsync();
        }

        var printer = new FakePrinter();
        using var runtime = new PackingRuntimeService(new UnitOfWorkFactory(cs), new FakeScanner(), printer, new FakeConfig(), new ZplTemplateRenderer());
        await runtime.StartAsync(1);
        await AssertThrowsAsync<InvalidOperationException>(() => runtime.RetryPrintAsync());
        await runtime.StopAsync();
        await runtime.StartAsync(2);
        await AssertThrowsAsync<InvalidOperationException>(() => runtime.RetryPrintAsync());
        Assert(printer.Labels.Count == 0, "Partial Carton/Pallet must never send a print command.");
        await using var verify = PrinterManagerDbContext.Create(cs);
        Assert(!await verify.PrintAttempts.AnyAsync(), "Rejected partial units must not create print attempts.");
        Console.WriteLine("Packing runtime full-only print guard passed.");
    }
    finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task VerifyBusyScanIsRejectedAsync()
{
    string path = Path.Combine(Path.GetTempPath(), $"ghnet-packing-busy-{Guid.NewGuid():N}.db");
    try
    {
        string cs = $"Data Source={path}";
        await using (var db = PrinterManagerDbContext.Create(cs))
        {
            await db.Database.MigrateAsync();
            var job = new Job { Name = "Busy scan", ProductsPerCarton = 1, CartonsPerPallet = 2, TotalData = 2 };
            db.Jobs.Add(job); await db.SaveChangesAsync();
            db.Products.AddRange(new Product { JobId = job.Id, Code = "P-1", SourceRowIndex = 1 }, new Product { JobId = job.Id, Code = "P-2", SourceRowIndex = 2 });
            db.Cartons.AddRange(new Carton { JobId = job.Id, Code = "C-1", SourceRowIndex = 1 }, new Carton { JobId = job.Id, Code = "C-2", SourceRowIndex = 2 });
            db.Pallets.Add(new Pallet { JobId = job.Id, Code = "L-1", SourceRowIndex = 1 }); await db.SaveChangesAsync();
        }
        var scanner = new BusyScanner(); var printer = new BlockingPrinter();
        using var runtime = new PackingRuntimeService(new UnitOfWorkFactory(cs), scanner, printer, new FakeConfig(), new ZplTemplateRenderer());
        await runtime.StartAsync(1);
        scanner.Raise("P-1"); await printer.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scanner.Raise("P-2");
        printer.Release.TrySetResult();
        for (int i = 0; i < 100; i++)
        {
            await using var check = PrinterManagerDbContext.Create(cs);
            if (await check.ScanEvents.AnyAsync(x => x.ScannedCode == "P-2" && x.ResultCode == ScanResultCode.Busy)) break;
            await Task.Delay(20);
        }
        await using (var verify = PrinterManagerDbContext.Create(cs))
        {
            Assert(await verify.ScanEvents.AnyAsync(x => x.ScannedCode == "P-2" && x.ResultCode == ScanResultCode.Busy), "A scan received while busy must be audited as Busy.");
            Assert((await verify.Products.SingleAsync(x => x.Code == "P-2")).Status == ProductStatus.Available, "A busy scan must not mutate packing state or enter a queue.");
        }
        for (int i = 0; i < 100 && runtime.IsBusy; i++) await Task.Delay(20);
        Console.WriteLine("Packing runtime busy-scan regression passed.");
    }
    finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
}

sealed class FakeScanner : IScannerDevice
{ public bool IsConnected => true; public string? CurrentPortName => "COM1"; public event EventHandler<ScannerCodeEventArgs>? CodeReceived; public event EventHandler? ConnectionStateChanged; public event EventHandler<Exception>? ReadFailed; public IReadOnlyList<string> GetAvailablePortNames()=>["COM1"]; public Task<bool> ConnectAsync(ScannerConfig c,CancellationToken t=default)=>Task.FromResult(true); public Task DisconnectAsync(CancellationToken t=default)=>Task.CompletedTask; public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
sealed class FakePrinter : IZebraPrinterDevice
{ public List<string> Labels { get; }=[]; public bool IsConnected=>true; public string? CurrentIp=>"127.0.0.1"; public int CurrentPort=>9100; public event EventHandler? ConnectionStateChanged; public Task<bool> ConnectAsync(string i,int p,TimeSpan x,CancellationToken t=default)=>Task.FromResult(true); public Task DisconnectAsync(CancellationToken t=default)=>Task.CompletedTask; public Task SendZplAsync(string z,TimeSpan x,CancellationToken t=default){ Labels.Add(z.Contains("C-1")?"C-1":"L-1"); return Task.CompletedTask;} public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
sealed class FakeConfig : IZebraPrinterConfigService
{ private readonly PrinterConfig _c=new(){Ip="127.0.0.1",Port=9100,CartonZplTemplate="^XA^FD{{CODE}}^FS^XZ",PalletZplTemplate="^XA^FD{{CODE}}^FS^XZ"}; public Task<PrinterConfig?> GetAsync(CancellationToken t=default)=>Task.FromResult<PrinterConfig?>(_c); public Task<ZebraPrinterApplyResult> ApplyAsync(PrinterConfig c,CancellationToken t=default)=>throw new NotSupportedException(); public Task<bool> ConnectSavedAsync(CancellationToken t=default)=>Task.FromResult(true); public Task TestPrintAsync(bool c,CancellationToken t=default)=>Task.CompletedTask; }
sealed class BusyScanner : IScannerDevice
{ public bool IsConnected=>true; public string? CurrentPortName=>"COM1"; public event EventHandler<ScannerCodeEventArgs>? CodeReceived; public event EventHandler? ConnectionStateChanged; public event EventHandler<Exception>? ReadFailed; public void Raise(string code)=>CodeReceived?.Invoke(this,new ScannerCodeEventArgs{Code=code}); public IReadOnlyList<string> GetAvailablePortNames()=>["COM1"]; public Task<bool> ConnectAsync(ScannerConfig c,CancellationToken t=default)=>Task.FromResult(true); public Task DisconnectAsync(CancellationToken t=default)=>Task.CompletedTask; public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
sealed class BlockingPrinter : IZebraPrinterDevice
{ public TaskCompletionSource SendStarted{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously); public bool IsConnected=>true; public string? CurrentIp=>"127.0.0.1"; public int CurrentPort=>9100; public event EventHandler? ConnectionStateChanged; public Task<bool> ConnectAsync(string i,int p,TimeSpan x,CancellationToken t=default)=>Task.FromResult(true); public Task DisconnectAsync(CancellationToken t=default)=>Task.CompletedTask; public async Task SendZplAsync(string z,TimeSpan x,CancellationToken t=default){SendStarted.TrySetResult();await Release.Task.WaitAsync(t);} public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
