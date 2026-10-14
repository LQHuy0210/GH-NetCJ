using GH_Net.Licensing;
using NSec.Cryptography;
using System.Security.Cryptography;
using GH_Net.Service.Abstractions;
using GH_Net.Service.Implements;

(byte[] privateKey, byte[] publicKey) = CreateKeys();
var keys = new Dictionary<string, byte[]> { ["K01"] = publicKey };
var codec = new LicenseTokenCodec();
DateTime now = new(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
const string hardwareId = "GHW1-ABCDE-FGHJK-MNPQR-STUVW-XYZ01";
var payload = new LicensePayload
{
    LicenseId = Guid.NewGuid(),
    Product = LicenseTokenCodec.Product,
    Customer = "Smoke Customer",
    HardwareId = hardwareId,
    IssuedAtUtc = now,
    NotBeforeUtc = now.AddMinutes(-1),
    ExpiresAtUtc = now.AddYears(1),
    Features = LicenseFeature.Printer | LicenseFeature.Scanner | LicenseFeature.Export
};

try
{
    Assert((int)LicenseFeature.Printer == 1 && (int)LicenseFeature.Camera == 2 &&
           (int)LicenseFeature.Export == 4 && (int)LicenseFeature.Plc == 8 &&
           (int)LicenseFeature.Scanner == 16, "Authoritative Base feature assignments changed.");
    foreach (object claim in new object[] { "Camera", 2, "All", 15, "Scanner", 16 })
    {
        LicenseFeature expected = claim switch
        {
            "Camera" or 2 => LicenseFeature.Camera,
            "All" or 15 => (LicenseFeature)15,
            _ => LicenseFeature.Scanner
        };
        var result = codec.VerifyToken(SignFeatureClaim(payload, claim, privateKey), keys, hardwareId, now);
        Assert(result.IsValid && result.Payload!.Features == expected,
            $"Legacy feature claim {claim} changed entitlement.");
    }
    for (int mask = 0; mask < 32; mask++)
    {
        var result = codec.VerifyToken(codec.CreateToken(payload with { Features = (LicenseFeature)mask }, "K01", privateKey), keys, hardwareId, now);
        Assert(result.IsValid && result.Payload!.Features == (LicenseFeature)mask,
            $"Five-feature mask {mask} failed round trip.");
    }
    string token = codec.CreateToken(payload, "K01", privateKey);
    Assert(token.StartsWith("GHN1.K01.", StringComparison.Ordinal), "Token version/key prefix changed.");
    Assert(codec.VerifyToken(token, keys, hardwareId, now).IsValid, "Valid token was rejected.");
    Assert(codec.VerifyToken("  " + token + "\r\n", keys, hardwareId, now).IsValid,
        "Copy/paste whitespace was not accepted.");
    Assert(codec.VerifyToken(token, keys, hardwareId + "X", now).Status ==
           LicenseValidationStatus.HardwareMismatch, "Wrong HWID was accepted.");
    Assert(codec.VerifyToken(token, keys, hardwareId, now.AddYears(2)).Status ==
           LicenseValidationStatus.Expired, "Expired token was accepted.");
    Assert(codec.VerifyToken(token.Replace("K01", "K02", StringComparison.Ordinal), keys, hardwareId, now).Status ==
           LicenseValidationStatus.UnknownKey, "Unknown signing key was accepted.");

    string[] tamperedParts = token.Split('.');
    char replacement = tamperedParts[2][0] == 'A' ? 'B' : 'A';
    tamperedParts[2] = replacement + tamperedParts[2][1..];
    string tampered = string.Join('.', tamperedParts);
    Assert(codec.VerifyToken(tampered, keys, hardwareId, now).Status ==
           LicenseValidationStatus.InvalidSignature, "Tampered token was accepted.");

    DateTime runtimeNow = DateTime.UtcNow;
    var runtimePayload = payload with
    {
        LicenseId = Guid.NewGuid(),
        IssuedAtUtc = runtimeNow,
        NotBeforeUtc = runtimeNow.AddMinutes(-1),
        ExpiresAtUtc = runtimeNow.AddDays(30),
        Features = LicenseFeature.Printer
    };
    string runtimeToken = codec.CreateToken(runtimePayload, "K01", privateKey);
    var storage = new FakeLicenseStorage([runtimeToken]);
    await using (var manager = new LicenseManager(
                     new FakeHardwareIdProvider(hardwareId),
                     storage,
                     codec,
                     keys))
    {
        await manager.InitializeAsync();
        Assert(manager.Current.IsValid && manager.HasFeature(LicenseFeature.Printer),
            "LicenseManager did not load the valid machine license.");
        Assert(!manager.HasFeature(LicenseFeature.Scanner),
            "LicenseManager granted an unlicensed feature.");

        string scannerToken = codec.CreateToken(runtimePayload with
        {
            LicenseId = Guid.NewGuid(),
            ExpiresAtUtc = runtimeNow.AddDays(10),
            Features = LicenseFeature.Scanner
        }, "K01", privateKey);
        Assert((await manager.ActivateAsync(scannerToken)).IsValid,
            "LicenseManager rejected a valid renewal token.");
        Assert(manager.HasFeature(LicenseFeature.Printer | LicenseFeature.Scanner),
            "LicenseManager did not preserve beneficial entitlements across tokens.");
    }

    var rollbackStorage = new FakeLicenseStorage(
        [runtimeToken],
        new LicenseTimeState(
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow.AddHours(1),
            2,
            hardwareId));
    await using (var rollbackManager = new LicenseManager(
                     new FakeHardwareIdProvider(hardwareId),
                     rollbackStorage,
                     codec,
                     keys))
    {
        await rollbackManager.InitializeAsync();
        Assert(rollbackManager.Current.Status == LicenseValidationStatus.ClockRollback,
            "LicenseManager did not detect clock rollback.");
    }

    if (OperatingSystem.IsWindows())
    {
        var hardwareProvider = new WindowsHardwareIdProvider();
        string first = hardwareProvider.GetHardwareId();
        string second = hardwareProvider.GetHardwareId();
        Assert(first == second && first.StartsWith("GHW1-", StringComparison.Ordinal),
            "Windows HWID was unstable or unversioned.");
    }

    Console.WriteLine("License smoke tests passed: Ed25519/token/tamper/HWID/expiry/key-rotation/renewal/rollback");
}

finally
{
    CryptographicOperations.ZeroMemory(privateKey);
    CryptographicOperations.ZeroMemory(publicKey);
}

static string SignFeatureClaim(LicensePayload payload, object claim, byte[] privateKey)
{
    var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
    var json = System.Text.Json.JsonSerializer.SerializeToNode(payload, options)!;
    json["features"] = claim is string name
        ? System.Text.Json.Nodes.JsonValue.Create(name)
        : System.Text.Json.Nodes.JsonValue.Create((int)claim);
    static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    string part = Encode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
    string input = $"GHN1.K01.{part}";
    using Key key = Key.Import(SignatureAlgorithm.Ed25519, privateKey, KeyBlobFormat.RawPrivateKey);
    return $"{input}.{Encode(SignatureAlgorithm.Ed25519.Sign(key, System.Text.Encoding.UTF8.GetBytes(input)))}";
}
static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static (byte[] PrivateKey, byte[] PublicKey) CreateKeys()
{
    var creation = new KeyCreationParameters
    {
        ExportPolicy = KeyExportPolicies.AllowPlaintextExport
    };
    using Key key = Key.Create(SignatureAlgorithm.Ed25519, creation);
    return (
        key.Export(KeyBlobFormat.RawPrivateKey),
        key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
}

sealed class FakeHardwareIdProvider(string value) : IHardwareIdProvider
{
    public string GetHardwareId() => value;
}

sealed class FakeLicenseStorage(
    IReadOnlyList<string>? tokens = null,
    LicenseTimeState? timeState = null) : ILicenseStorage
{
    private List<string> _tokens = tokens?.ToList() ?? [];
    private LicenseTimeState? _timeState = timeState;

    public Task<IReadOnlyList<string>> LoadTokensAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_tokens.ToArray());
    public Task SaveTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken ct = default)
    {
        _tokens = tokens.ToList();
        return Task.CompletedTask;
    }
    public Task<LicenseTimeState?> LoadTimeStateAsync(CancellationToken ct = default)
        => Task.FromResult(_timeState);
    public Task SaveTimeStateAsync(LicenseTimeState state, CancellationToken ct = default)
    {
        _timeState = state;
        return Task.CompletedTask;
    }
}
