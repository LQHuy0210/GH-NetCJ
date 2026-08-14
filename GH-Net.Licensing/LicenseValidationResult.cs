namespace GH_Net.Licensing;

/// <summary>Validation outcome and verified claims, when available.</summary>
public sealed record LicenseValidationResult(
    LicenseValidationStatus Status,
    LicensePayload? Payload = null,
    string? KeyId = null)
{
    public bool IsValid => Status == LicenseValidationStatus.Valid && Payload is not null;
}
