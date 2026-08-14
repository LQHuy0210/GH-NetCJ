using GH_Net.Core.Enums;

namespace GH_Net.Service.Abstractions;

/// <summary>Coordinates the single packing pipeline and its global devices.</summary>
public interface IJobRuntimeCoordinator
{
    int? ActiveJobId { get; }
    JobStatus CurrentStatus { get; }

    /// <summary>Raised when the coordinated runtime status changes.</summary>
    event Action<JobStatus> StatusChanged;

    /// <summary>Raised when background combined completion fails.</summary>
    event Action<Exception> RuntimeFailed;

    /// <summary>Starts or resumes a packing Job after validating global devices.</summary>
    Task StartAsync(int jobId, CancellationToken ct = default);

    /// <summary>Stops and flushes every pipeline participating in the active Job.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Releases the current Job context while retaining global connections.</summary>
    Task ReleaseJobAsync(CancellationToken ct = default);
}
