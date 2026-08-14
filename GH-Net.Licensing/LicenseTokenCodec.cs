using NSec.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GH_Net.Licensing;

/// <summary>Creates and verifies versioned Ed25519 GH-Net license tokens.</summary>
public sealed class LicenseTokenCodec
{
    public const string Product = "GH-Net";
    public const string TokenVersion = "GHN1";
    public const int MaximumTokenLength = 8192;

    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Signs a payload with a raw Ed25519 private key.</summary>
    public string CreateToken(
        LicensePayload payload,
        string keyId,
        ReadOnlySpan<byte> rawPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKeyId(keyId);
        if (!string.Equals(payload.Product, Product, StringComparison.Ordinal))
            throw new ArgumentException($"Product must be {Product}.", nameof(payload));

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        string payloadPart = Base64Url.Encode(payloadBytes);
        byte[] signingInput = BuildSigningInput(keyId, payloadPart);
        using Key key = Key.Import(Algorithm, rawPrivateKey, KeyBlobFormat.RawPrivateKey);
        byte[] signature = Algorithm.Sign(key, signingInput);
        return $"{TokenVersion}.{keyId}.{payloadPart}.{Base64Url.Encode(signature)}";
    }

    /// <summary>Verifies signature, product, machine binding and validity period.</summary>
    public LicenseValidationResult VerifyToken(
        string? token,
        IReadOnlyDictionary<string, byte[]> publicKeys,
        string expectedHardwareId,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new LicenseValidationResult(LicenseValidationStatus.Missing);
        if (token.Length > MaximumTokenLength)
            return new LicenseValidationResult(LicenseValidationStatus.Malformed);

        try
        {
            string compact = RemoveWhitespace(token);
            string[] parts = compact.Split('.');
            if (parts.Length != 4 ||
                !string.Equals(parts[0], TokenVersion, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(parts[1]) ||
                string.IsNullOrWhiteSpace(parts[2]) ||
                string.IsNullOrWhiteSpace(parts[3]))
                return new LicenseValidationResult(LicenseValidationStatus.Malformed);

            string keyId = parts[1];
            if (!publicKeys.TryGetValue(keyId, out byte[]? publicKeyBytes))
                return new LicenseValidationResult(LicenseValidationStatus.UnknownKey, KeyId: keyId);

            byte[] signature = Base64Url.Decode(parts[3]);
            PublicKey publicKey = PublicKey.Import(
                Algorithm,
                publicKeyBytes,
                KeyBlobFormat.RawPublicKey);
            if (!Algorithm.Verify(publicKey, BuildSigningInput(keyId, parts[2]), signature))
                return new LicenseValidationResult(LicenseValidationStatus.InvalidSignature, KeyId: keyId);

            byte[] payloadBytes = Base64Url.Decode(parts[2]);
            LicensePayload? payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null || payload.LicenseId == Guid.Empty)
                return new LicenseValidationResult(LicenseValidationStatus.Malformed, KeyId: keyId);
            if (!string.Equals(payload.Product, Product, StringComparison.Ordinal))
                return new LicenseValidationResult(LicenseValidationStatus.WrongProduct, payload, keyId);
            if (!FixedEquals(payload.HardwareId, expectedHardwareId))
                return new LicenseValidationResult(LicenseValidationStatus.HardwareMismatch, payload, keyId);

            DateTime now = EnsureUtc(utcNow);
            if (now < EnsureUtc(payload.NotBeforeUtc))
                return new LicenseValidationResult(LicenseValidationStatus.NotYetValid, payload, keyId);
            if (payload.ExpiresAtUtc is { } expiry && now > EnsureUtc(expiry))
                return new LicenseValidationResult(LicenseValidationStatus.Expired, payload, keyId);

            return new LicenseValidationResult(LicenseValidationStatus.Valid, payload, keyId);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return new LicenseValidationResult(LicenseValidationStatus.Malformed);
        }
    }

    private static byte[] BuildSigningInput(string keyId, string payloadPart)
        => Encoding.UTF8.GetBytes($"{TokenVersion}.{keyId}.{payloadPart}");

    private static string RemoveWhitespace(string value)
        => new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static bool FixedEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                   leftBytes,
                   rightBytes);
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) ||
            keyId.Length > 16 ||
            keyId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Key ID must contain 1-16 ASCII letters or digits.", nameof(keyId));
    }
}
