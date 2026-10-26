namespace GH_Net.Service.Abstractions;

public sealed record TechnicalUnlockResult(bool Succeeded, int RetryAfterSeconds = 0);

/// <summary>Local technical-settings lock; independent of licensing and production Jobs.</summary>
public interface ITechnicalSettingsAccess
{
    bool IsUnlocked { get; }
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
    Task ConfigureAsync(string code, CancellationToken ct = default);
    Task<TechnicalUnlockResult> UnlockAsync(string code, CancellationToken ct = default);
    void Lock();
    void EnsureUnlocked();
}
