using GH_Net.Service.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;

namespace GH_Net.Service.Implements;

/// <summary>Atomically persists machine-scoped license data protected by Windows DPAPI.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiLicenseStorage : ILicenseStorage
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GH-Net|Licensing|v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly string _licensesPath;
    private readonly string _statePath;
    private readonly string _stateBackupPath;

    public DpapiLicenseStorage()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GH-Net",
            "Licensing");
        _licensesPath = Path.Combine(_directory, "licenses.dat");
        _statePath = Path.Combine(_directory, "state.dat");
        _stateBackupPath = Path.Combine(_directory, "state.bak");
    }

    public async Task<IReadOnlyList<string>> LoadTokensAsync(CancellationToken ct = default)
    {
        var document = await ReadProtectedAsync<LicenseStoreDocument>(_licensesPath, ct)
            .ConfigureAwait(false);
        return document?.Tokens ?? [];
    }

    public Task SaveTokensAsync(
        IReadOnlyCollection<string> tokens,
        CancellationToken ct = default)
        => WriteProtectedAsync(
            _licensesPath,
            new LicenseStoreDocument(tokens.Distinct(StringComparer.Ordinal).ToArray()),
            ct);

    public async Task<LicenseTimeState?> LoadTimeStateAsync(CancellationToken ct = default)
    {
        LicenseTimeState? primary = await TryReadStateAsync(_statePath, ct).ConfigureAwait(false);
        LicenseTimeState? backup = await TryReadStateAsync(_stateBackupPath, ct).ConfigureAwait(false);
        if (primary is null && backup is null &&
            (File.Exists(_statePath) || File.Exists(_stateBackupPath)))
            throw new CryptographicException("Both license clock-state copies are unreadable.");
        if (primary is null)
            return backup;
        if (backup is null)
            return primary;
        return primary.MaxObservedUtc >= backup.MaxObservedUtc ? primary : backup;
    }

    public async Task SaveTimeStateAsync(LicenseTimeState state, CancellationToken ct = default)
    {
        await WriteProtectedAsync(_statePath, state, ct).ConfigureAwait(false);
        await WriteProtectedAsync(_stateBackupPath, state, ct).ConfigureAwait(false);
    }

    private static async Task<LicenseTimeState?> TryReadStateAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            return await ReadProtectedAsync<LicenseTimeState>(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    private static async Task<T?> ReadProtectedAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return default;
        byte[] protectedBytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        byte[] plainBytes = ProtectedData.Unprotect(
            protectedBytes,
            Entropy,
            DataProtectionScope.LocalMachine);
        try
        {
            return JsonSerializer.Deserialize<T>(plainBytes, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private async Task WriteProtectedAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        byte[] plainBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(
                plainBytes,
                Entropy,
                DataProtectionScope.LocalMachine);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, protectedBytes, ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private sealed record LicenseStoreDocument(string[] Tokens);
}
