using System.Security.Cryptography;
using System.Text;
using GH_Net.Repository.Abstractions;
using GH_Net.Service.Abstractions;

namespace GH_Net.Service.Implements;

/// <summary>Salted PBKDF2 credential protected with Windows DPAPI for the current Windows user.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class TechnicalSettingsAccess(ITechnicalLockStore store, TimeProvider? timeProvider = null) : ITechnicalSettingsAccess
{
    private const int Iterations = 300_000;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GH-Net.CJ.PlcTechnicalAccess.v1");
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _record;
    private bool _loaded;
    private long _unlockedUntil;
    private DateTimeOffset _retryAt;
    private int _failures;
    public bool IsUnlocked => _clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _unlockedUntil);
    public void Lock() => Interlocked.Exchange(ref _unlockedUntil, 0);
    public void EnsureUnlocked() { if (!IsUnlocked) throw new UnauthorizedAccessException("Technical settings are locked."); }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_loaded) return;
        byte[]? envelope = await store.ReadAsync(ct);
        if (envelope is not null)
        {
            _record = ProtectedData.Unprotect(envelope, Entropy, DataProtectionScope.CurrentUser);
            if (_record.Length != 65 || _record[0] != 1) { _record = null; throw new InvalidDataException("Technical access storage is invalid."); }
        }
        _loaded = true;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await LoadAsync(ct); return _record is not null; }
        finally { _gate.Release(); }
    }

    public async Task ConfigureAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 8 or > 64)
            throw new ArgumentException("The technical code must contain 8–64 characters.");
        await _gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            if (_record is not null) EnsureUnlocked();
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            byte[] hash = await Task.Run(() => Rfc2898DeriveBytes.Pbkdf2(code, salt, Iterations, HashAlgorithmName.SHA256, 32), ct);
            byte[] record = [1, .. salt, .. hash];
            try { await store.WriteAsync(ProtectedData.Protect(record, Entropy, DataProtectionScope.CurrentUser), ct); }
            catch { CryptographicOperations.ZeroMemory(record); throw; }
            if (_record is not null) CryptographicOperations.ZeroMemory(_record);
            _record = record; CryptographicOperations.ZeroMemory(hash);
            _failures = 0; _retryAt = default;
            Interlocked.Exchange(ref _unlockedUntil, _clock.GetUtcNow().AddMinutes(5).UtcTicks);
        }
        finally { _gate.Release(); }
    }

    public async Task<TechnicalUnlockResult> UnlockAsync(string code, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            if (_record is null) return new(false);
            var now = _clock.GetUtcNow();
            if (now < _retryAt) return new(false, (int)Math.Ceiling((_retryAt - now).TotalSeconds));
            byte[] salt = _record.AsSpan(1, 32).ToArray();
            bool valid = false;
            if (code.Length is >= 8 and <= 64)
            {
                byte[] hash = await Task.Run(() => Rfc2898DeriveBytes.Pbkdf2(code, salt, Iterations, HashAlgorithmName.SHA256, 32), ct);
                valid = CryptographicOperations.FixedTimeEquals(hash, _record.AsSpan(33, 32));
                CryptographicOperations.ZeroMemory(hash);
            }
            if (valid)
            {
                _failures = 0; _retryAt = default;
                Interlocked.Exchange(ref _unlockedUntil, now.AddMinutes(5).UtcTicks);
                return new(true);
            }
            Lock();
            if (++_failures >= 5) { _failures = 0; _retryAt = now.AddSeconds(30); return new(false, 30); }
            return new(false);
        }
        finally { _gate.Release(); }
    }
}
