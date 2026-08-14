using GH_Net.Repository.Entities;

namespace GH_Net.Service.Abstractions;

public sealed record ScannerApplyResult(ScannerConfig Config, bool Connected);

public interface IScannerConfigService
{
    Task<ScannerConfig?> GetAsync(CancellationToken ct = default);
    IReadOnlyList<string> GetAvailablePortNames();
    Task<ScannerApplyResult> ApplyAsync(ScannerConfig config, CancellationToken ct = default);
    Task<bool> ConnectSavedAsync(CancellationToken ct = default);
}
