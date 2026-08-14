namespace GH_Net.Service.Abstractions;

/// <summary>Machine-local protected persistence for license tokens and clock state.</summary>
public interface ILicenseStorage
{
    Task<IReadOnlyList<string>> LoadTokensAsync(CancellationToken ct = default);
    Task SaveTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken ct = default);
    Task<LicenseTimeState?> LoadTimeStateAsync(CancellationToken ct = default);
    Task SaveTimeStateAsync(LicenseTimeState state, CancellationToken ct = default);
}

public sealed record LicenseTimeState(
    DateTime MaxObservedUtc,
    DateTime LastRunUtc,
    long RunCount,
    string HardwareId);
