namespace GH_Net.Licensing;

/// <summary>Claims signed by the private key held by the license issuer.</summary>
public sealed record LicensePayload
{
    public required Guid LicenseId { get; init; }
    public required string Product { get; init; }
    public required string Customer { get; init; }
    public required string HardwareId { get; init; }
    public required DateTime IssuedAtUtc { get; init; }
    public required DateTime NotBeforeUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public required LicenseFeature Features { get; init; }
}
