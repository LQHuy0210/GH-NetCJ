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
    private readonly IAutomationService? _automation;
    private PackingMode _mode;
    private string _noReadToken = "NOREAD";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _subscribed;
    private long _session;
    private int _cameraPending;
    private int _cameraOverflow;
    private int _syncPending;

    public PackingRuntimeService(IUnitOfWorkFactory factory, IScannerDevice scanner,
        IZebraPrinterDevice printer, IZebraPrinterConfigService printerConfig,
        IZplTemplateRenderer renderer, IAutomationService? automation = null)
    { _factory = factory; _scanner = scanner; _printer = printer; _printerConfig = printerConfig; _renderer = renderer; _automation = automation; }

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
            if (IsRunning) return;
            using var uow = _factory.Create();
            var job = await uow.Repository<Job>().FindAsync(jobId, ct) ?? throw new InvalidOperationException("Job not found.");
            if (job.Status == JobStatus.Completed) throw new InvalidOperationException("Job cannot be started.");
            if ((job.UsePlc || job.Mode == PackingMode.SemiAutomatic) && _automation is null)
                throw new InvalidOperationException("Camera/PLC runtime is unavailable.");
            _mode = job.Mode;
            _cameraPending = _cameraOverflow = 0;
            if (_automation is not null)
            {
                _noReadToken = (await _automation.GetAsync(ct)).NoReadToken;
                await _automation.StartAsync(job, ct);
            }
            if (job.PackingWorkflowState is PackingWorkflowState.Stopped or PackingWorkflowState.Faulted)
                job.PackingWorkflowState = PackingWorkflowState.WaitingProduct;
            job.Status = JobStatus.Running; job.StartedAt ??= DateTime.Now; job.StoppedAt = null;
            await uow.SaveChangesAsync(ct);
            ActiveJobId = jobId; IsRunning = true;
            Subscribe();
            await PublishAsync("Ready to scan.", null, null, ct);
            await UpdateLineStateAsync(ct);
        }
        catch { await PauseAfterFailureAsync(); throw; }
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
            if (_automation is not null) await _automation.StopAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public Task HandleScanAsync(string code, CancellationToken ct = default) => ProcessAsync(code ?? "", false, ct);

    private async void OnCodeReceived(object? sender, ScannerCodeEventArgs e)
    {
        try { await ProcessAsync(e.Code, true, CancellationToken.None, false, ActiveJobId, Volatile.Read(ref _session)); }
        catch (Exception ex) { RuntimeFailed?.Invoke(this, ex); }
    }

    private async void OnCameraFrame(object? sender, GH_Net.Repository.Abstractions.CameraFrameEventArgs e)
    {
        long session = Volatile.Read(ref _session);
        bool first = Interlocked.CompareExchange(ref _cameraPending, 1, 0) == 0;
        if (!first && Interlocked.CompareExchange(ref _cameraOverflow, 1, 0) != 0) return;
        int? receivedJobId = ActiveJobId;
        try
        {
            if (first) await ProcessAsync(e.Value, true, CancellationToken.None, true, receivedJobId, session);
            else
            {
                await _gate.WaitAsync();
                try
                {
                    if (!IsRunning || receivedJobId is not int jobId || session != Volatile.Read(ref _session)) return;
                    long sequence = await NextCameraSequenceAsync(jobId, CancellationToken.None);
                    await RejectAsync(jobId, e.Value, ScanResultCode.Busy, "Camera overrun; quarantine unaccepted products before restart.", CancellationToken.None, true, sequence);
                    if (_automation is not null) await _automation.RejectAsync(sequence);
                    await PauseAfterFailureAsync();
                    RuntimeFailed?.Invoke(this, new IOException("Camera overrun; quarantine unaccepted products before restart."));
                }
                catch { await PauseAfterFailureAsync(); throw; }
                finally { _gate.Release(); }
            }
        }
        catch (Exception ex) { RuntimeFailed?.Invoke(this, ex); }
        finally
        {
            if (session == Volatile.Read(ref _session) && first) Interlocked.Exchange(ref _cameraPending, 0);
        }
    }

    private async Task ProcessAsync(string code, bool rejectWhenBusy, CancellationToken ct, bool camera = false, int? receivedJobId = null, long? receivedSession = null)
    {
        if (!IsRunning || ActiveJobId is not int jobId) return;
        if (rejectWhenBusy && !await _gate.WaitAsync(0, ct))
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (!IsRunning || ActiveJobId != jobId || (receivedJobId.HasValue && receivedJobId != jobId) || (receivedSession.HasValue && receivedSession != Volatile.Read(ref _session))) return;
                long? busySequence = camera ? await NextCameraSequenceAsync(jobId, ct) : null;
                await RejectAsync(jobId, code, ScanResultCode.Busy, "Busy; scan was rejected, not queued.", ct, camera, busySequence);
                if (camera)
                {
                    if (_automation is not null) await _automation.RejectAsync(busySequence!.Value, ct);
                    throw new IOException("Camera overrun: remove the unaccepted product and restart the Job.");
                }
            }
            catch { await PauseAfterFailureAsync(); throw; }
            finally { _gate.Release(); }
            return;
        }
        if (!rejectWhenBusy) await _gate.WaitAsync(ct);
        try
        {
            if (!IsRunning || ActiveJobId != jobId || (receivedJobId.HasValue && receivedJobId != jobId) || (receivedSession.HasValue && receivedSession != Volatile.Read(ref _session))) return;
            long? sequence = camera ? await NextCameraSequenceAsync(jobId, ct) : null;
            if (_automation is not null) await _automation.SetStateAsync(LineState.Hold, ct);
            try
            {
                if (camera && (string.IsNullOrEmpty(code) || code == _noReadToken))
                    throw new ScanRejectedException(ScanResultCode.NotFound, "Camera could not read the product code.");
                if (string.IsNullOrEmpty(code)) throw new ScanRejectedException(ScanResultCode.NotFound, "Empty scan.");
                using var uow = _factory.Create();
                await using var tx = await uow.BeginTransactionAsync(ct);
                var job = await uow.Repository<Job>().FindAsync(jobId, ct) ?? throw new InvalidOperationException("Job not found.");
                if (job.PackingWorkflowState is PackingWorkflowState.CartonWaitingPrint or PackingWorkflowState.PalletWaitingPrint)
                    throw new ScanRejectedException(ScanResultCode.PrintRequired, "Print the pending label before scanning.");
                var expected = PackingPolicy.ExpectedCodeType(job.PackingWorkflowState);
                if ((camera && expected != ScanCodeType.Product) || (!camera && _mode == PackingMode.SemiAutomatic && expected == ScanCodeType.Product))
                    throw new ScanRejectedException(ScanResultCode.WrongCodeType, "Semi-automatic mode uses the camera for Products and scanner for Carton/Pallet confirmation.");
                if (expected == ScanCodeType.Product) await ScanProductAsync(uow, job, code, ct, camera, sequence);
                else if (expected == ScanCodeType.Carton) await ConfirmCartonAsync(uow, job, code, ct);
                else await ConfirmPalletAsync(uow, job, code, ct);
                await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                if (job.PackingWorkflowState is PackingWorkflowState.CartonWaitingPrint or PackingWorkflowState.PalletWaitingPrint)
                    if (_automation is not null) await _automation.SetStateAsync(LineState.Full, ct);
                await PrintPendingAsync(jobId, ct);
                await PublishAsync("Scan processed.", code, ScanResultCode.Accepted, ct);
                await UpdateLineStateAsync(ct);
            }
            catch (ScanRejectedException ex)
            {
                await RejectAsync(jobId, code, ex.Code, ex.Message, ct, camera, sequence);
                if (camera && _automation is not null) await _automation.RejectAsync(sequence!.Value, ct);
                await UpdateLineStateAsync(ct);
            }
        }
        catch { await PauseAfterFailureAsync(); throw; }
        finally { _gate.Release(); }
    }

    private static async Task ScanProductAsync(IUnitOfWork uow, Job job, string code, CancellationToken ct, bool camera = false, long? sequence = null)
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
        AddScan(uow, job.Id, code, ScanCodeType.Product, ScanResultCode.Accepted, null, camera, sequence);
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

    public Task RetryPrintAsync(CancellationToken ct = default) => LockedActionAsync(async jobId =>
    {
        await PrintPendingAsync(jobId, ct);
        await PublishAsync("Print attempt processed.", null, null, ct);
        await UpdateLineStateAsync(ct);
    }, ct);

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

    private async Task RejectAsync(int jobId, string code, ScanResultCode result, string message, CancellationToken ct, bool camera = false, long? sequence = null)
    { using var uow = _factory.Create(); var job = await uow.Repository<Job>().FindAsync(jobId, ct); var expected = job is null || job.PackingWorkflowState is PackingWorkflowState.Stopped or PackingWorkflowState.Completed or PackingWorkflowState.Faulted or PackingWorkflowState.CartonWaitingPrint or PackingWorkflowState.PalletWaitingPrint ? ScanCodeType.Product : PackingPolicy.ExpectedCodeType(job.PackingWorkflowState); AddScan(uow, jobId, code, expected, result, message, camera, sequence); await uow.SaveChangesAsync(ct); await PublishAsync(message, code, result, ct); }
    private async Task RecordBusyAsync(int jobId, string code, CancellationToken ct) => await RejectAsync(jobId, code, ScanResultCode.Busy, "Scanner busy; scan was not queued.", ct);
    private static void AddScan(IUnitOfWork uow, int jobId, string code, ScanCodeType expected, ScanResultCode result, string? detail, bool camera = false, long? sequence = null) => uow.Repository<ScanEvent>().Add(new() { JobId = jobId, ScannedCode = code, ExpectedCodeType = expected, ResultCode = result, Detail = detail, Source = camera ? "Camera" : "Scanner", CameraSequence = sequence });
    private static async Task<ScanRejectedException> ClassifyAsync(IUnitOfWork uow, int jobId, string code, ScanCodeType expected, CancellationToken ct)
    { bool wrong = expected != ScanCodeType.Product && await uow.Repository<Product>().Query().AnyAsync(x => x.JobId == jobId && x.Code == code, ct) || expected != ScanCodeType.Carton && await uow.Repository<Carton>().Query().AnyAsync(x => x.JobId == jobId && x.Code == code, ct) || expected != ScanCodeType.Pallet && await uow.Repository<Pallet>().Query().AnyAsync(x => x.JobId == jobId && x.Code == code, ct); return new(wrong ? ScanResultCode.WrongCodeType : ScanResultCode.UnexpectedCode, wrong ? "Scanned code type is not expected now." : "Code is not valid for the current job/state."); }
    private async Task<long> NextCameraSequenceAsync(int jobId, CancellationToken ct)
    {
        using var uow = _factory.Create();
        var job = await uow.Repository<Job>().FindAsync(jobId, ct) ?? throw new InvalidOperationException("Job not found.");
        if (job.LastCameraSequence >= GH_Net.Repository.Devices.PlcCommandCodec.MaximumIndex)
            throw new InvalidOperationException("Camera sequence exhausted; create another Job.");
        job.LastCameraSequence++;
        await uow.SaveChangesAsync(ct);
        return job.LastCameraSequence;
    }

    private async Task UpdateLineStateAsync(CancellationToken ct)
    {
        if (_automation is null || Current is null) return;
        if (Volatile.Read(ref _cameraOverflow) != 0) { await _automation.SetStateAsync(LineState.Fault, ct); return; }
        if (Current.WorkflowState == PackingWorkflowState.Completed) { await _automation.StopAsync(ct); return; }
        await _automation.SetStateAsync(Current.WorkflowState switch
        {
            PackingWorkflowState.WaitingProduct or PackingWorkflowState.CartonFilling => LineState.Ready,
            PackingWorkflowState.CartonWaitingConfirmation or PackingWorkflowState.PalletWaitingConfirmation => LineState.Full,
            PackingWorkflowState.CartonWaitingPrint or PackingWorkflowState.PalletWaitingPrint => LineState.Fault,
            _ => LineState.Hold
        }, ct);
    }

    private async Task PauseAfterFailureAsync()
    {
        Unsubscribe(); IsRunning = false;
        try
        {
            if (ActiveJobId is int jobId)
            {
                using var uow = _factory.Create();
                var job = await uow.Repository<Job>().FindAsync(jobId);
                if (job?.Status == JobStatus.Running) { job.Status = JobStatus.Paused; job.StoppedAt = DateTime.Now; await uow.SaveChangesAsync(); }
            }
        }
        finally
        {
            if (_automation is not null)
            {
                try { await _automation.SetStateAsync(LineState.Fault); } catch { }
                try { await _automation.StopAsync(); } catch { }
            }
        }
    }

    private async void OnAutomationFailed(object? sender, Exception error)
    {
        long session = Volatile.Read(ref _session);
        await _gate.WaitAsync();
        try { if (IsRunning && session == Volatile.Read(ref _session)) { await PauseAfterFailureAsync(); RuntimeFailed?.Invoke(this, error); } }
        catch (Exception ex) { RuntimeFailed?.Invoke(this, ex); }
        finally { _gate.Release(); }
    }

    private async void OnSynchronizationRequested(object? sender, EventArgs args)
    {
        if (Interlocked.Exchange(ref _syncPending, 1) != 0) return;
        int? jobId = ActiveJobId;
        long session = Volatile.Read(ref _session);
        await _gate.WaitAsync();
        try
        {
            if (!IsRunning || jobId is null || ActiveJobId != jobId || _automation is null || session != Volatile.Read(ref _session)) return;
            using var uow = _factory.Create();
            var job = await uow.Repository<Job>().FindAsync(jobId.Value);
            if (job is not null) await _automation.SynchronizeAsync(job.LastCameraSequence);
        }
        catch (Exception ex) { await PauseAfterFailureAsync(); RuntimeFailed?.Invoke(this, ex); }
        finally { Interlocked.Exchange(ref _syncPending, 0); _gate.Release(); }
    }

    private void Subscribe()
    {
        if (_subscribed) return; Interlocked.Increment(ref _session); _scanner.CodeReceived += OnCodeReceived;
        if (_automation is not null) { _automation.FrameReceived += OnCameraFrame; _automation.Failed += OnAutomationFailed; _automation.SynchronizationRequested += OnSynchronizationRequested; }
        _subscribed = true;
    }
    private void Unsubscribe()
    {
        if (!_subscribed) return; Interlocked.Increment(ref _session); _scanner.CodeReceived -= OnCodeReceived;
        if (_automation is not null) { _automation.FrameReceived -= OnCameraFrame; _automation.Failed -= OnAutomationFailed; _automation.SynchronizationRequested -= OnSynchronizationRequested; }
        _subscribed = false;
    }
    public void Dispose() { Unsubscribe(); _gate.Dispose(); }
    private sealed class ScanRejectedException(ScanResultCode code, string message) : Exception(message) { public ScanResultCode Code { get; } = code; }
}
