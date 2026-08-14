using GH_Net.Core.Enums;
using GH_Net.Repository.Entities;

namespace GH_Net.Service.Abstractions;

public sealed record PackingCodeStatistics(
    int CheckedProducts,
    int RemainingProducts,
    int CheckedCartons,
    int RemainingCartons,
    int CheckedPallets,
    int RemainingPallets);

public interface IJobService
{
    Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default);
    Task<Job?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default);
    Task<PackingCodeStatistics> GetPackingCodeStatisticsAsync(int jobId, CancellationToken ct = default);
    Task UpdateStatusAsync(int jobId, JobStatus newStatus, CancellationToken ct = default);
    Task RecoverCrashedJobsAsync(CancellationToken ct = default);
    Task DeleteJobAsync(int jobId, CancellationToken ct = default);
    Task DeleteJobsAsync(IReadOnlyCollection<int> jobIds, CancellationToken ct = default);
}
