namespace GH_Net.Service.Implements;

/// <summary>Trusted issuer public keys embedded in the customer application.</summary>
public static class DesktopLicenseKeyRing
{
    // Provisioned once from GH_Net.LicenseGenerator. Private keys never enter this repository.
    public const string K01PublicKeyBase64 = "YYqdf6yBwgjxXAkO3TdzpkClO6wlz+/sJiyMHlYLpSI=";

    public static IReadOnlyDictionary<string, byte[]> Create()
        => new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["K01"] = Convert.FromBase64String(K01PublicKeyBase64)
        };
}
