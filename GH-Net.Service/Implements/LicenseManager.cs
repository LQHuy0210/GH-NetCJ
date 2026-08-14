using GH_Net.Licensing;
using GH_Net.Service.Abstractions;

namespace GH_Net.Service.Implements;

/// <inheritdoc cref="ILicenseManager"/>
public sealed class LicenseManager : ILicenseManager
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromMinutes(5);
    private readonly ILicenseStorage _storage;
    private readonly LicenseTokenCodec _codec;
    private readonly IReadOnlyDictionary<string, byte[]> _publicKeys;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PeriodicTimer _timer = new(MonitorInterval);
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private List<string> _tokens = [];
    private LicenseTimeState? _timeState;
    private bool _disposed;

    public LicenseManager(
        IHardwareIdProvider hardwareIdProvider,
        ILicenseStorage storage,
        LicenseTokenCodec codec,
        IReadOnlyDictionary<string, byte[]> publicKeys)
    {
        _storage = storage;
        _codec = codec;
        _publicKeys = publicKeys;
        Current = Empty(
            LicenseValidationStatus.Missing,
            hardwareIdProvider.GetHardwareId());
    }

    public LicenseSnapshot Current { get; private set; }
    public event EventHandler? LicenseChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _tokens = (await _storage.LoadTokensAsync(ct).ConfigureAwait(false)).ToList();
            _timeState = await _storage.LoadTimeStateAsync(ct).ConfigureAwait(false);
            await EvaluateAndPersistTimeAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
            StartMonitor();
        }
        catch
        {
            SetCurrent(Empty(LicenseValidationStatus.StorageError, Current.HardwareId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LicenseValidationResult> ActivateAsync(
        string token,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTime now = DateTime.UtcNow;
            if (IsClockRolledBack(now))
                return new LicenseValidationResult(LicenseValidationStatus.ClockRollback);

            LicenseValidationResult result = _codec.VerifyToken(
                token,
                _publicKeys,
                Current.HardwareId,
                now);
            if (!result.IsValid)
                return result;

            string compact = new(token.Where(character => !char.IsWhiteSpace(character)).ToArray());
            _tokens.RemoveAll(existing => string.Equals(existing, compact, StringComparison.Ordinal));
            _tokens.Add(compact);
            await _storage.SaveTokensAsync(_tokens, ct).ConfigureAwait(false);
            await EvaluateAndPersistTimeAsync(now, ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EvaluateAndPersistTimeAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasFeature(LicenseFeature feature) => Current.HasFeature(feature);

    private async Task EvaluateAndPersistTimeAsync(DateTime now, CancellationToken ct)
    {
        if (IsClockRolledBack(now))
        {
            SetCurrent(Empty(LicenseValidationStatus.ClockRollback, Current.HardwareId));
            return;
        }

        var valid = new List<LicensePayload>();
        LicenseValidationStatus fallback = _tokens.Count == 0
            ? LicenseValidationStatus.Missing
            : LicenseValidationStatus.Malformed;
        foreach (string token in _tokens)
        {
            LicenseValidationResult result = _codec.VerifyToken(
                token,
                _publicKeys,
                Current.HardwareId,
                now);
            if (result.IsValid)
                valid.Add(result.Payload!);
            else if (fallback is LicenseValidationStatus.Malformed or LicenseValidationStatus.Missing)
                fallback = result.Status;
        }

        LicenseSnapshot snapshot;
        if (valid.Count == 0)
        {
            snapshot = Empty(fallback, Current.HardwareId);
        }
        else
        {
            LicenseFeature features = valid.Aggregate(
                LicenseFeature.None,
                (current, payload) => current | payload.Features);
            LicensePayload display = valid
                .OrderByDescending(payload => payload.ExpiresAtUtc ?? DateTime.MaxValue)
                .First();
            snapshot = new LicenseSnapshot
            {
                Status = LicenseValidationStatus.Valid,
                HardwareId = Current.HardwareId,
                Customer = display.Customer,
                ExpiresAtUtc = valid.Any(payload => payload.ExpiresAtUtc is null)
                    ? null
                    : valid.Max(payload => payload.ExpiresAtUtc),
                Features = features,
                ActiveLicenses = valid
            };
        }

        SetCurrent(snapshot);
        _timeState = new LicenseTimeState(
            Max(now, _timeState?.MaxObservedUtc ?? now),
            now,
            (_timeState?.RunCount ?? 0) + 1,
            Current.HardwareId);
        await _storage.SaveTimeStateAsync(_timeState, ct).ConfigureAwait(false);
    }

    private bool IsClockRolledBack(DateTime now)
        => _timeState is not null &&
           string.Equals(_timeState.HardwareId, Current.HardwareId, StringComparison.Ordinal) &&
           now + ClockTolerance < _timeState.MaxObservedUtc;

    private void StartMonitor()
    {
        if (_monitorTask is not null)
            return;
        _monitorCts = new CancellationTokenSource();
        _monitorTask = MonitorAsync(_monitorCts.Token);
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await RefreshAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch
        {
            SetCurrent(Empty(LicenseValidationStatus.StorageError, Current.HardwareId));
        }
    }

    private void SetCurrent(LicenseSnapshot value)
    {
        if (Current == value)
            return;
        Current = value;
        LicenseChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LicenseSnapshot Empty(LicenseValidationStatus status, string hardwareId)
        => new() { Status = status, HardwareId = hardwareId };

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _monitorCts?.Cancel();
        if (_monitorTask is not null)
        {
            try { await _monitorTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _monitorCts?.Dispose();
        _timer.Dispose();
        _gate.Dispose();
    }
}
