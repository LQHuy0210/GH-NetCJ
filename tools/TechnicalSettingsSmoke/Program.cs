using GH_Net.Repository.Abstractions;
using GH_Net.Service.Implements;

var store = new MemoryStore(); var clock = new TestClock();
var access = new TechnicalSettingsAccess(store, clock);
Assert(!await access.IsConfiguredAsync() && !access.IsUnlocked, "First use must be locked without a default code.");
await ThrowsAsync<ArgumentException>(() => access.ConfigureAsync("short"));
await access.ConfigureAsync("test-only-secret-A");
Assert(await access.IsConfiguredAsync() && access.IsUnlocked, "Local provisioning must grant a temporary session.");
access.Lock(); Throws<UnauthorizedAccessException>(() => access.EnsureUnlocked());
Assert(!(await access.UnlockAsync("wrong-secret")).Succeeded, "Incorrect code unlocked settings.");
Assert((await access.UnlockAsync("test-only-secret-A")).Succeeded, "Correct code could not unlock settings.");
clock.Advance(TimeSpan.FromMinutes(5));
Assert(!access.IsUnlocked, "Technical access must expire after five minutes.");
await ThrowsAsync<UnauthorizedAccessException>(() => access.ConfigureAsync("test-only-secret-B"));
for (int i = 0; i < 4; i++) Assert(!(await access.UnlockAsync("wrong-secret")).Succeeded, "Wrong code unexpectedly succeeded.");
var limited = await access.UnlockAsync("wrong-secret");
Assert(limited.RetryAfterSeconds > 0, "Repeated failed attempts must be rate limited.");
Assert(!(await access.UnlockAsync("test-only-secret-A")).Succeeded, "Lockout was bypassed with a correct code.");
clock.Advance(TimeSpan.FromSeconds(31));
Assert((await access.UnlockAsync("test-only-secret-A")).Succeeded, "Unlock after cooldown failed.");
await access.ConfigureAsync("test-only-secret-B"); access.Lock();
Assert(!(await access.UnlockAsync("test-only-secret-A")).Succeeded, "Previous code remained valid after rotation.");
Assert((await access.UnlockAsync("test-only-secret-B")).Succeeded, "Rotated code did not work.");

var restarted = new TechnicalSettingsAccess(store, clock);
Assert(await restarted.IsConfiguredAsync() && !restarted.IsUnlocked, "Restart must retain the credential but not the unlocked session.");
Assert((await restarted.UnlockAsync("test-only-secret-B")).Succeeded, "DPAPI credential did not round trip.");
Assert(store.Value is not null && !System.Text.Encoding.UTF8.GetString(store.Value).Contains("test-only-secret"), "Credential storage leaked plaintext.");

var damaged = new MemoryStore { Value = [1, 2, 3] };
var failClosed = new TechnicalSettingsAccess(damaged, clock);
await ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => failClosed.IsConfiguredAsync());
Assert(!failClosed.IsUnlocked, "Corrupt credentials must not unlock or become first-time setup.");
var failingStore = new MemoryStore { FailWrite = true };
var failing = new TechnicalSettingsAccess(failingStore, clock);
await ThrowsAsync<IOException>(() => failing.ConfigureAsync("test-only-secret-C"));
Assert(!failing.IsUnlocked && !await failing.IsConfiguredAsync(), "Failed provisioning incorrectly granted access.");
Console.WriteLine("Technical settings smoke passed: no default credential, encrypted persistence, verification, expiry, cooldown, rotation, restart and fail-closed storage.");

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
sealed class MemoryStore : ITechnicalLockStore
{
    public byte[]? Value { get; set; }
    public bool FailWrite { get; set; }
    public Task<byte[]?> ReadAsync(CancellationToken ct = default) => Task.FromResult(Value?.ToArray());
    public Task WriteAsync(byte[] value, CancellationToken ct = default)
    { if (FailWrite) throw new IOException("Injected write failure."); Value = value.ToArray(); return Task.CompletedTask; }
}
sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
