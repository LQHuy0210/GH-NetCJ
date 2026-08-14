using GH_Net.Licensing;

namespace GH_Net.Service.Abstractions;

/// <summary>Validates, activates and continuously monitors the effective machine license.</summary>
public interface ILicenseManager : IAsyncDisposable
{
    LicenseSnapshot Current { get; }
    event EventHandler? LicenseChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task<LicenseValidationResult> ActivateAsync(string token, CancellationToken ct = default);
    Task RefreshAsync(CancellationToken ct = default);
    bool HasFeature(LicenseFeature feature);
}
