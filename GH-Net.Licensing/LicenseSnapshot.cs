namespace GH_Net.Licensing;

/// <summary>Effective license state exposed to the application.</summary>
public sealed record LicenseSnapshot
{
    public required LicenseValidationStatus Status { get; init; }
    public required string HardwareId { get; init; }
    public string Customer { get; init; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; init; }
    public LicenseFeature Features { get; init; }
    public IReadOnlyList<LicensePayload> ActiveLicenses { get; init; } = [];
    public bool IsValid => Status == LicenseValidationStatus.Valid;

    public bool HasFeature(LicenseFeature feature)
        => IsValid && (Features & feature) == feature;
}
