using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GH_Net.Service.Implements;

public sealed class JobService : IJobService
{
    private readonly IUnitOfWork _uow;
    private static readonly IReadOnlyDictionary<JobStatus, IReadOnlySet<JobStatus>> Transitions =
        new Dictionary<JobStatus, IReadOnlySet<JobStatus>>
        {
            [JobStatus.NewlyCreated] = new HashSet<JobStatus> { JobStatus.Running, JobStatus.Paused },
            [JobStatus.Running] = new HashSet<JobStatus> { JobStatus.Paused, JobStatus.Completed },
            [JobStatus.Paused] = new HashSet<JobStatus> { JobStatus.Running }
        };

    public JobService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default)
        => await _uow.Repository<Job>().Query().AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct).ConfigureAwait(false);

    public async Task<Job?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default)
        => await _uow.Repository<Job>().Query().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);

    public async Task<PackingCodeStatistics> GetPackingCodeStatisticsAsync(
        int jobId,
        CancellationToken ct = default)
    {
        if (!await _uow.Repository<Job>().Query().AsNoTracking().AnyAsync(x => x.Id == jobId, ct))
            throw new InvalidOperationException($"Job {jobId} was not found.");

        int checkedProducts = await _uow.Repository<Product>().Query().AsNoTracking()
            .CountAsync(x => x.JobId == jobId && x.Status != ProductStatus.Available, ct);
        int remainingProducts = await _uow.Repository<Product>().Query().AsNoTracking()
            .CountAsync(x => x.JobId == jobId && x.Status == ProductStatus.Available, ct);
        int checkedCartons = await _uow.Repository<Carton>().Query().AsNoTracking()
            .CountAsync(x => x.JobId == jobId && x.Status == CartonStatus.Completed, ct);
        int remainingCartons = await _uow.Repository<Carton>().Query().AsNoTracking()
            .CountAsync(x => x.JobId == jobId && x.Status != CartonStatus.Completed, ct);
        int checkedPallets = await _uow.Repository<Pallet>().Query().AsNoTracking()
            .CountAsync(x => x.JobId == jobId && x.Status == PalletStatus.Completed, ct);
        int remainingPallets = await _uow.Repository<Pallet>().Query().AsNoTracking()
            .CountAsync(x => x.JobId == jobId && x.Status != PalletStatus.Completed, ct);

        return new(
            checkedProducts,
            remainingProducts,
            checkedCartons,
            remainingCartons,
            checkedPallets,
            remainingPallets);
    }

    public async Task UpdateStatusAsync(int jobId, JobStatus newStatus, CancellationToken ct = default)
    {
        Job job = await _uow.Repository<Job>().FindAsync(jobId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job {jobId} was not found.");
        if (!Transitions.TryGetValue(job.Status, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException($"Cannot move Job from {job.Status} to {newStatus}.");
        job.Status = newStatus;
        if (newStatus == JobStatus.Running) { job.StartedAt ??= DateTime.Now; job.StoppedAt = null; }
        if (newStatus == JobStatus.Paused) job.StoppedAt = DateTime.Now;
        if (newStatus == JobStatus.Completed) { job.CompletedAt = DateTime.Now; job.StoppedAt = DateTime.Now; }
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RecoverCrashedJobsAsync(CancellationToken ct = default)
    {
        var jobs = await _uow.Repository<Job>().Query().Where(x => x.Status == JobStatus.Running).ToListAsync(ct).ConfigureAwait(false);
        foreach (var job in jobs) { job.Status = JobStatus.Paused; job.StoppedAt = DateTime.Now; }
        if (jobs.Count > 0) await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteJobAsync(int jobId, CancellationToken ct = default)
    {
        Job job = await _uow.Repository<Job>().FindAsync(jobId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job {jobId} was not found.");
        if (job.Status == JobStatus.Running) throw new InvalidOperationException("Stop the running Job before deleting it.");
        _uow.Repository<Job>().Remove(job);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteJobsAsync(IReadOnlyCollection<int> jobIds, CancellationToken ct = default)
    {
        var ids = jobIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        var jobs = await _uow.Repository<Job>().Query().Where(x => ids.Contains(x.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (jobs.Any(x => x.Status == JobStatus.Running)) throw new InvalidOperationException("Stop running Jobs before deleting them.");
        _uow.Repository<Job>().RemoveRange(jobs);
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
