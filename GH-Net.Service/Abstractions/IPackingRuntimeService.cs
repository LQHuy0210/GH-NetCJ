using GH_Net.Core.Enums;

namespace GH_Net.Service.Abstractions;

public sealed record PackingRuntimeSnapshot(
    int JobId,
    PackingWorkflowState WorkflowState,
    string? CartonCode,
    int CartonCount,
    int ProductsPerCarton,
    string? PalletCode,
    int PalletCount,
    int CartonsPerPallet,
    int PackedProducts,
    int TotalProducts,
    int CheckedProducts,
    int RemainingProducts,
    int CheckedCartons,
    int RemainingCartons,
    int CheckedPallets,
    int RemainingPallets,
    string? LastCode,
    ScanResultCode? LastResult,
    string Message,
    bool CanRetryPrint,
    IReadOnlyList<string> RecentProducts);

public interface IPackingRuntimeService
{
    int? ActiveJobId { get; }
    bool IsRunning { get; }
    bool IsBusy { get; }
    PackingRuntimeSnapshot? Current { get; }
    event EventHandler<PackingRuntimeSnapshot>? SnapshotChanged;
    event EventHandler<Exception>? RuntimeFailed;
    Task StartAsync(int jobId, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task HandleScanAsync(string code, CancellationToken ct = default);
    Task RetryPrintAsync(CancellationToken ct = default);
}
