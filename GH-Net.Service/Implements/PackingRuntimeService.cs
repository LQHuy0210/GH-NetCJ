using GH_Net.Core.Enums;
using GH_Net.Core.Packing;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GH_Net.Service.Implements;

public sealed class PackingRuntimeService : IPackingRuntimeService, IDisposable
{
    private readonly IUnitOfWorkFactory _factory;
    private readonly IScannerDevice _scanner;
    private readonly IZebraPrinterDevice _printer;
    private readonly IZebraPrinterConfigService _printerConfig;
    private readonly IZplTemplateRenderer _renderer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _subscribed;

    public PackingRuntimeService(IUnitOfWorkFactory factory, IScannerDevice scanner,
        IZebraPrinterDevice printer, IZebraPrinterConfigService printerConfig,
        IZplTemplateRenderer renderer)
    { _factory = factory; _scanner = scanner; _printer = printer; _printerConfig = printerConfig; _renderer = renderer; }

    public int? ActiveJobId { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsBusy => _gate.CurrentCount == 0;
    public PackingRuntimeSnapshot? Current { get; private set; }
    public event EventHandler<PackingRuntimeSnapshot>? SnapshotChanged;
    public event EventHandler<Exception>? RuntimeFailed;

    public async Task StartAsync(int jobId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsRunning && ActiveJobId != jobId) throw new InvalidOperationException("Another packing job is running.");
            using var uow = _factory.Create();
            var job = await uow.Repository<Job>().FindAsync(jobId, ct) ?? throw new InvalidOperationException("Job not found.");
            if (job.Status == JobStatus.Completed) throw new InvalidOperationException("Job cannot be started.");
            if (job.PackingWorkflowState is PackingWorkflowState.Stopped or PackingWorkflowState.Faulted)
                job.PackingWorkflowState = PackingWorkflowState.WaitingProduct;
            job.Status = JobStatus.Running; job.StartedAt ??= DateTime.Now; job.StoppedAt = null;
            await uow.SaveChangesAsync(ct);
            ActiveJobId = jobId; IsRunning = true;
            Subscribe();
            await PublishAsync("Ready to scan.", null, null, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Unsubscribe();
            if (ActiveJobId is int jobId)
            {
                using var uow = _factory.Create();
                var job = await uow.Repository<Job>().FindAsync(jobId, ct);
                if (job is not null && job.Status == JobStatus.Running) { job.Status = JobStatus.Paused; job.StoppedAt = DateTime.Now; await uow.SaveChangesAsync(ct); }
            }
            IsRunning = false;
        }
        finally { _gate.Release(); }
    }

    public Task HandleScanAsync(string code, CancellationToken ct = default) => ProcessAsync(code ?? "", false, ct);

    private async void OnCodeReceived(object? sender, ScannerCodeEventArgs e)
    {
        try { await ProcessAsync(e.Code, true, CancellationToken.None); }
        catch (Exception ex) { RuntimeFailed?.Invoke(this, ex); }
    }

    private async Task ProcessAsync(string code, bool rejectWhenBusy, CancellationToken ct)
    {
        if (!IsRunning || ActiveJobId is not int jobId) return;
        if (rejectWhenBusy && !await _gate.WaitAsync(0, ct))
        {
            await _gate.WaitAsync(ct);
            try { await RecordBusyAsync(jobId, code, ct); }
            finally { _gate.Release(); }
            return;
        }
        if (!rejectWhenBusy) await _gate.WaitAsync(ct);
        try
        {
            if (string.IsNullOrEmpty(code)) { await RejectAsync(jobId, code, ScanResultCode.NotFound, "Empty scan.", ct); return; }
            using var uow = _factory.Create();
            await using var tx = await uow.BeginTransactionAsync(ct);
            var job = await uow.Repository<Job>().FindAsync(jobId, ct) ?? throw new InvalidOperationException("Job not found.");
            var expected = PackingPolicy.ExpectedCodeType(job.PackingWorkflowState);
            if (expected == ScanCodeType.Product) await ScanProductAsync(uow, job, code, ct);
            else if (expected == ScanCodeType.Carton) await ConfirmCartonAsync(uow, job, code, ct);
            else await ConfirmPalletAsync(uow, job, code, ct);
            await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            await PrintPendingAsync(jobId, ct);
            await PublishAsync("Scan processed.", code, ScanResultCode.Accepted, ct);
        }
        catch (ScanRejectedException ex) { await RejectAsync(jobId, code, ex.Code, ex.Message, ct); }
        finally { _gate.Release(); }
    }

    private static async Task ScanProductAsync(IUnitOfWork uow, Job job, string code, CancellationToken ct)
    {
        var product = await uow.Repository<Product>().Query().SingleOrDefaultAsync(x => x.JobId == job.Id && x.Code == code, ct);
        if (product is null) throw await ClassifyAsync(uow, job.Id, code, ScanCodeType.Product, ct);
        if (product.Status != ProductStatus.Available) throw new ScanRejectedException(ScanResultCode.Duplicate, "Product was already scanned in this job.");
        var carton = await uow.Repository<Carton>().Query().Where(x => x.JobId == job.Id && x.Status == CartonStatus.Filling).OrderBy(x => x.SourceRowIndex).FirstOrDefaultAsync(ct);
        if (carton is null)
        {
            carton = await uow.Repository<Carton>().Query().Where(x => x.JobId == job.Id && x.Status == CartonStatus.Available).OrderBy(x => x.SourceRowIndex).FirstOrDefaultAsync(ct);
            if (carton is null) throw new ScanRejectedException(ScanResultCode.NoCartonData, "No carton code remains; product was not accepted.");
            carton.Status = CartonStatus.Filling; carton.FillingStartedAt = DateTime.Now; carton.Sequence = await uow.Repository<Carton>().Query().CountAsync(x => x.JobId == job.Id && x.Status != CartonStatus.Available, ct) + 1;
        }
        product.CartonId = carton.Id; product.Status = ProductStatus.Scanned; product.ScannedAt = DateTime.Now; carton.ProductCount++;
        if (carton.ProductCount == job.ProductsPerCarton) { carton.Status = CartonStatus.WaitingPrint; job.PackingWorkflowState = PackingWorkflowState.CartonWaitingPrint; }
        else job.PackingWorkflowState = PackingWorkflowState.CartonFilling;
        AddScan(uow, job.Id, code, ScanCodeType.Product, ScanResultCode.Accepted, null);
    }

    private static async Task ConfirmCartonAsync(IUnitOfWork uow, Job job, string code, CancellationToken ct)
    {
        var carton = await uow.Repository<Carton>().Query().SingleOrDefaultAsync(x => x.JobId == job.Id && x.Status == CartonStatus.Printed, ct);
        if (carton is null || carton.Code != code) throw await ClassifyAsync(uow, job.Id, code, ScanCodeType.Carton, ct);
        var pallet = await uow.Repository<Pallet>().Query().Where(x => x.JobId == job.Id && x.Status == PalletStatus.Filling).OrderBy(x => x.SourceRowIndex).FirstOrDefaultAsync(ct);
        if (pallet is null)
        {
            pallet = await uow.Repository<Pallet>().Query().Where(x => x.JobId == job.Id && x.Status == PalletStatus.Available).OrderBy(x => x.SourceRowIndex).FirstOrDefaultAsync(ct);
            if (pallet is null) throw new ScanRejectedException(ScanResultCode.NoPalletData, "No pallet code remains; carton confirmation was not accepted.");
            pallet.Status = PalletStatus.Filling; pallet.FillingStartedAt = DateTime.Now; pallet.Sequence = await uow.Repository<Pallet>().Query().CountAsync(x => x.JobId == job.Id && x.Status != PalletStatus.Available, ct) + 1;
        }
        carton.Status = CartonStatus.Completed; carton.CompletedAt = DateTime.Now; carton.PalletId = pallet.Id;
        foreach (var p in await uow.Repository<Product>().Query().Where(x => x.CartonId == carton.Id).ToListAsync(ct)) { p.Status = ProductStatus.Packed; p.PackedAt = DateTime.Now; }
        pallet.CartonCount++;
        if (pallet.CartonCount == job.CartonsPerPallet) { pallet.Status = PalletStatus.WaitingPrint; job.PackingWorkflowState = PackingWorkflowState.PalletWaitingPrint; }
        else job.PackingWorkflowState = PackingWorkflowState.WaitingProduct;
        AddScan(uow, job.Id, code, ScanCodeType.Carton, ScanResultCode.Accepted, null);
    }

    private static async Task ConfirmPalletAsync(IUnitOfWork uow, Job job, string code, CancellationToken ct)
    {
        var pallet = await uow.Repository<Pallet>().Query().SingleOrDefaultAsync(x => x.JobId == job.Id && x.Status == PalletStatus.Printed, ct);
        if (pallet is null || pallet.Code != code) throw await ClassifyAsync(uow, job.Id, code, ScanCodeType.Pallet, ct);
        pallet.Status = PalletStatus.Completed; pallet.CompletedAt = DateTime.Now;
        bool remaining = await uow.Repository<Product>().Query().AnyAsync(x => x.JobId == job.Id && x.Status != ProductStatus.Packed, ct);
        job.PackingWorkflowState = remaining ? PackingWorkflowState.WaitingProduct : PackingWorkflowState.Completed;
        if (!remaining) { job.Status = JobStatus.Completed; job.CompletedAt = DateTime.Now; job.StoppedAt = DateTime.Now; }
        AddScan(uow, job.Id, code, ScanCodeType.Pallet, ScanResultCode.Accepted, null);
    }

    public Task RetryPrintAsync(CancellationToken ct = default) => LockedActionAsync(async jobId => await PrintPendingAsync(jobId, ct), ct);

    private async Task LockedActionAsync(Func<int, Task> action, CancellationToken ct)
    { if (!IsRunning || ActiveJobId is not int id) throw new InvalidOperationException("Packing job is not running."); await _gate.WaitAsync(ct); try { await action(id); } finally { _gate.Release(); } }

    private async Task PrintPendingAsync(int jobId, CancellationToken ct)
    {
        using var uow = _factory.Create(); var job = await uow.Repository<Job>().FindAsync(jobId, ct) ?? throw new InvalidOperationException("Job not found.");
        var carton = await uow.Repository<Carton>().Query().SingleOrDefaultAsync(x => x.JobId == jobId && x.Status == CartonStatus.WaitingPrint, ct);
        var pallet = carton is null ? await uow.Repository<Pallet>().Query().SingleOrDefaultAsync(x => x.JobId == jobId && x.Status == PalletStatus.WaitingPrint, ct) : null;
        if (carton is null && pallet is null) return;
        if (carton is not null && carton.ProductCount != job.ProductsPerCarton)
            throw new InvalidOperationException(
                $"Carton {carton.Code} has {carton.ProductCount}/{job.ProductsPerCarton} Products; no print command was sent.");
        if (pallet is not null && pallet.CartonCount != job.CartonsPerPallet)
            throw new InvalidOperationException(
                $"Pallet {pallet.Code} has {pallet.CartonCount}/{job.CartonsPerPallet} Cartons; no print command was sent.");
        var config = await _printerConfig.GetAsync(ct) ?? throw new InvalidOperationException("Zebra printer configuration is missing.");
        string code = carton?.Code ?? pallet!.Code; string template = carton is not null ? config.CartonZplTemplate ?? "" : config.PalletZplTemplate ?? "";
        string zpl = _renderer.Render(template, code);
        int attempt = await uow.Repository<PrintAttempt>().Query().CountAsync(
            x => x.JobId == jobId && (carton != null ? x.CartonId == carton.Id : x.PalletId == pallet!.Id), ct) + 1;
        var log = new PrintAttempt { JobId = jobId, CartonId = carton?.Id, PalletId = pallet?.Id, AttemptNumber = attempt, ZplSnapshot = zpl };
        try
        {
            await _printer.SendZplAsync(zpl, TimeSpan.FromMilliseconds(config.SendTimeoutMs), ct); log.Succeeded = true;
            if (carton is not null) { carton.PrintCount++; carton.PrintedAt = DateTime.Now; carton.Status = CartonStatus.Printed; job.PackingWorkflowState = PackingWorkflowState.CartonWaitingConfirmation; }
            else { pallet!.PrintCount++; pallet.PrintedAt = DateTime.Now; pallet.Status = PalletStatus.Printed; job.PackingWorkflowState = PackingWorkflowState.PalletWaitingConfirmation; }
        }
        catch (Exception ex) { log.Error = ex.Message; }
        uow.Repository<PrintAttempt>().Add(log); await uow.SaveChangesAsync(ct);
    }

    private async Task PublishAsync(string message, string? code, ScanResultCode? result, CancellationToken ct)
    {
        if (ActiveJobId is not int id) return; using var uow = _factory.Create();
        var job = await uow.Repository<Job>().FindAsync(id, ct); if (job is null) return;
        var carton = await uow.Repository<Carton>().Query().Where(x => x.JobId == id && x.Status != CartonStatus.Available && x.Status != CartonStatus.Completed).OrderByDescending(x => x.SourceRowIndex).FirstOrDefaultAsync(ct);
        var pallet = await uow.Repository<Pallet>().Query().Where(x => x.JobId == id && x.Status != PalletStatus.Available && x.Status != PalletStatus.Completed).OrderByDescending(x => x.SourceRowIndex).FirstOrDefaultAsync(ct);
        int packed = await uow.Repository<Product>().Query().CountAsync(x => x.JobId == id && x.Status == ProductStatus.Packed, ct);
        int checkedProducts = await uow.Repository<Product>().Query().CountAsync(x => x.JobId == id && x.Status != ProductStatus.Available, ct);
        int remainingProducts = await uow.Repository<Product>().Query().CountAsync(x => x.JobId == id && x.Status == ProductStatus.Available, ct);
        int checkedCartons = await uow.Repository<Carton>().Query().CountAsync(x => x.JobId == id && x.Status == CartonStatus.Completed, ct);
        int remainingCartons = await uow.Repository<Carton>().Query().CountAsync(x => x.JobId == id && x.Status != CartonStatus.Completed, ct);
        int checkedPallets = await uow.Repository<Pallet>().Query().CountAsync(x => x.JobId == id && x.Status == PalletStatus.Completed, ct);
        int remainingPallets = await uow.Repository<Pallet>().Query().CountAsync(x => x.JobId == id && x.Status != PalletStatus.Completed, ct);
        var recent = await uow.Repository<Product>().Query().Where(x => x.JobId == id && x.ScannedAt != null).OrderByDescending(x => x.ScannedAt).Select(x => x.Code).Take(10).ToListAsync(ct);
        Current = new(id, job.PackingWorkflowState, carton?.Code, carton?.ProductCount ?? 0, job.ProductsPerCarton, pallet?.Code, pallet?.CartonCount ?? 0, job.CartonsPerPallet, packed, job.TotalData,
            checkedProducts, remainingProducts, checkedCartons, remainingCartons, checkedPallets, remainingPallets, code, result, message,
            job.PackingWorkflowState is PackingWorkflowState.CartonWaitingPrint or PackingWorkflowState.PalletWaitingPrint, recent);
        SnapshotChanged?.Invoke(this, Current);
        if (job.Status == JobStatus.Completed) { IsRunning = false; Unsubscribe(); }
    }

    private async Task RejectAsync(int jobId, string code, ScanResultCode result, string message, CancellationToken ct)
    { using var uow = _factory.Create(); var job = await uow.Repository<Job>().FindAsync(jobId, ct); var expected = job is null || job.PackingWorkflowState is PackingWorkflowState.Stopped or PackingWorkflowState.Completed or PackingWorkflowState.Faulted ? ScanCodeType.Product : PackingPolicy.ExpectedCodeType(job.PackingWorkflowState); AddScan(uow, jobId, code, expected, result, message); await uow.SaveChangesAsync(ct); await PublishAsync(message, code, result, ct); }
    private async Task RecordBusyAsync(int jobId, string code, CancellationToken ct) => await RejectAsync(jobId, code, ScanResultCode.Busy, "Scanner busy; scan was not queued.", ct);
    private static void AddScan(IUnitOfWork uow, int jobId, string code, ScanCodeType expected, ScanResultCode result, string? detail) => uow.Repository<ScanEvent>().Add(new() { JobId = jobId, ScannedCode = code, ExpectedCodeType = expected, ResultCode = result, Detail = detail });
    private static async Task<ScanRejectedException> ClassifyAsync(IUnitOfWork uow, int jobId, string code, ScanCodeType expected, CancellationToken ct)
    { bool wrong = expected != ScanCodeType.Product && await uow.Repository<Product>().Query().AnyAsync(x => x.JobId == jobId && x.Code == code, ct) || expected != ScanCodeType.Carton && await uow.Repository<Carton>().Query().AnyAsync(x => x.JobId == jobId && x.Code == code, ct) || expected != ScanCodeType.Pallet && await uow.Repository<Pallet>().Query().AnyAsync(x => x.JobId == jobId && x.Code == code, ct); return new(wrong ? ScanResultCode.WrongCodeType : ScanResultCode.UnexpectedCode, wrong ? "Scanned code type is not expected now." : "Code is not valid for the current job/state."); }
    private void Subscribe() { if (_subscribed) return; _scanner.CodeReceived += OnCodeReceived; _subscribed = true; }
    private void Unsubscribe() { if (!_subscribed) return; _scanner.CodeReceived -= OnCodeReceived; _subscribed = false; }
    public void Dispose() { Unsubscribe(); _gate.Dispose(); }
    private sealed class ScanRejectedException(ScanResultCode code, string message) : Exception(message) { public ScanResultCode Code { get; } = code; }
}
