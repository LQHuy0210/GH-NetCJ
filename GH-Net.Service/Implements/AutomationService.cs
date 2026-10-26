using GH_Net.Core.Enums;
using GH_Net.Licensing;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Devices;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace GH_Net.Service.Implements;

/// <summary>ZRP TCP framing/reject protocol, with CJ line commands and durable send outcomes.</summary>
public sealed class AutomationService : IAutomationService, IDisposable
{
    private readonly IUnitOfWorkFactory _factory;
    private readonly ICameraDevice _camera;
    private readonly IPlcDevice _plc;
    private readonly ILicenseManager _license;
    private readonly ITechnicalSettingsAccess _technicalAccess;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private AutomationConfig? _config;
    private Job? _job;
    private long _generation;
    private bool _stopping;

    public AutomationService(IUnitOfWorkFactory factory, ICameraDevice camera, IPlcDevice plc, ILicenseManager license, ITechnicalSettingsAccess technicalAccess)
    {
        _factory = factory; _camera = camera; _plc = plc; _license = license;
        _technicalAccess = technicalAccess;
        _camera.FrameReceived += OnFrame;
        _camera.Disconnected += OnCameraDisconnected;
        _camera.ConnectionStateChanged += OnCameraState;
        _plc.ConnectionStateChanged += OnPlcState;
        _plc.MessageReceived += OnPlcMessage;
    }

    public event EventHandler<CameraFrameEventArgs>? FrameReceived;
    public event EventHandler<Exception>? Failed;
    public event EventHandler? SynchronizationRequested;
    public event EventHandler? ConnectionStateChanged;
    public bool CameraConnected => _camera.IsConnected;
    public bool PlcConnected => _plc.IsConnected;

    public async Task<AutomationConfig> GetAsync(CancellationToken ct = default)
    {
        using var uow = _factory.Create();
        return await uow.Repository<AutomationConfig>().Query().AsNoTracking().SingleOrDefaultAsync(ct) ?? new();
    }

    public static void Validate(AutomationConfig config)
    {
        static bool Endpoint(string ip, int port) => IPAddress.TryParse(ip, out var address) &&
            !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.Broadcast) && port is > 0 and <= 65535;
        if (!Endpoint(config.CameraIp, config.CameraPort) || !Endpoint(config.PlcIp, config.PlcPort))
            throw new InvalidOperationException("Invalid camera/PLC IP address or port.");
        if (config.CameraIp == config.PlcIp && config.CameraPort == config.PlcPort)
            throw new InvalidOperationException("Camera and PLC require separate TCP endpoints.");
        if (!Enum.IsDefined(config.Framing) || !Enum.IsDefined(config.RejectMode) || config.TimeoutMs is < 100 or > 30000)
            throw new InvalidOperationException("Invalid PLC framing, reject mode or timeout (100–30000 ms).");
        foreach (string command in new[] { config.ReadyCommand, config.HoldCommand, config.FullCommand, config.FaultCommand, config.StopCommand })
            if (string.IsNullOrWhiteSpace(command) || command.Length > 128 || command.Any(c => c is < ' ' or > '~'))
                throw new InvalidOperationException("PLC commands must contain 1–128 printable ASCII characters without framing.");
        ValidateSensors(config.Sensors);
        if (config.CameraTerminator is not ("CR" or "LF" or "CRLF")) throw new InvalidOperationException("Invalid camera terminator.");
        if (string.IsNullOrWhiteSpace(config.NoReadToken) || config.NoReadToken.Length > 500 || config.NoReadToken.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidOperationException("Invalid camera no-read token.");
    }

    public Task ApplyAsync(AutomationConfig config, CancellationToken ct = default) => ApplyDeviceAsync(config, true, true, true, ct);
    public Task ApplyCameraAsync(AutomationConfig config, CancellationToken ct = default) => ApplyDeviceAsync(config, true, false, false, ct);
    public Task ApplyPlcAsync(AutomationConfig config, CancellationToken ct = default) => ApplyDeviceAsync(config, false, true, true, ct);
    public Task ApplyPlcConnectionAsync(AutomationConfig config, CancellationToken ct = default) => ApplyDeviceAsync(config, false, true, false, ct);

    private async Task ApplyDeviceAsync(AutomationConfig config, bool camera, bool plc, bool technical, CancellationToken ct)
    {
        if (!_license.Current.IsValid || (camera && !_license.HasFeature(LicenseFeature.Camera)) || (plc && !_license.HasFeature(LicenseFeature.Plc)))
            throw new InvalidOperationException("The selected device is not licensed.");
        await _sessionGate.WaitAsync(ct);
        try
        {
            using var uow = _factory.Create();
            if (technical) _technicalAccess.EnsureUnlocked();
            if (_job is not null || await uow.Repository<Job>().Query().AnyAsync(x => x.Status == JobStatus.Running, ct))
                throw new InvalidOperationException("Stop the running Job before changing device settings.");
            var repository = uow.Repository<AutomationConfig>();
            var saved = await repository.Query().SingleOrDefaultAsync(ct);
            if (saved is null) { saved = new(); repository.Add(saved); }
            // Merge only the selected tab; an unapplied draft in the other tab must not be saved.
            if (camera)
            {
                saved.CameraIp = config.CameraIp; saved.CameraPort = config.CameraPort; saved.NoReadToken = config.NoReadToken;
                saved.CameraTerminator = config.CameraTerminator;
            }
            if (plc)
            {
                saved.PlcIp = config.PlcIp; saved.PlcPort = config.PlcPort;
            }
            if (plc && technical)
            {
                saved.Framing = config.Framing;
                saved.RejectMode = config.RejectMode; saved.TimeoutMs = config.TimeoutMs;
                saved.ReadyCommand = config.ReadyCommand; saved.HoldCommand = config.HoldCommand;
                saved.FullCommand = config.FullCommand; saved.FaultCommand = config.FaultCommand; saved.StopCommand = config.StopCommand;
                saved.Sensors = config.Sensors.Select(sensor => new PlcSensorSettings
                {
                    Number = sensor.Number, DelayDistance = sensor.DelayDistance, IgnoreDistance = sensor.IgnoreDistance,
                    PulseCount = sensor.PulseCount, EncoderDiameter = sensor.EncoderDiameter,
                    Gap = sensor.Gap, ErrorCondition = sensor.ErrorCondition
                }).OrderBy(sensor => sensor.Number).ToList();
            }
            Validate(saved);
            await uow.SaveChangesAsync(ct);
            if (camera && !await _camera.ConnectAsync(saved.CameraIp, saved.CameraPort, TimeSpan.FromMilliseconds(saved.TimeoutMs), ct, saved.CameraTerminator))
                throw new InvalidOperationException("Camera settings saved; camera connection failed.");
            if (plc && !await _plc.ConnectAsync(saved.PlcIp, saved.PlcPort, TimeSpan.FromMilliseconds(saved.TimeoutMs), ct))
                throw new InvalidOperationException("PLC settings saved; PLC connection failed.");
        }
        finally { _sessionGate.Release(); }
    }

    private static void ValidateSensors(IReadOnlyCollection<PlcSensorSettings> sensors)
    {
        if (sensors.Count != 2 || !sensors.Select(sensor => sensor.Number).Order().SequenceEqual([1, 2]))
            throw new InvalidOperationException("PLC configuration requires Sensor 1 and Sensor 2.");
        foreach (var sensor in sensors)
        {
            if (sensor.PulseCount is < 1 or > 99999 || sensor.EncoderDiameter <= 0 || sensor.EncoderDiameter > 999.99m ||
                sensor.EncoderDiameter * 100 != decimal.Truncate(sensor.EncoderDiameter * 100) ||
                new[] { sensor.DelayDistance, sensor.IgnoreDistance, sensor.Gap, sensor.ErrorCondition }.Any(value => value is < 0 or > 99999))
                throw new InvalidOperationException($"Sensor {sensor.Number}: enter valid values for all six settings.");
        }
    }

    public async Task StartAsync(Job job, CancellationToken ct = default)
    {
        await _sessionGate.WaitAsync(ct);
        try
        {
            if (_job is not null) throw new InvalidOperationException("An automation session is already active.");
            GH_Net.Core.Packing.PackingPolicy.ValidateMode(job.Mode, job.UsePlc);
            if (!job.UsePlc && job.Mode == PackingMode.Manual) return;
            if (!_license.Current.IsValid || !_license.HasFeature(LicenseFeature.Plc) ||
                (job.Mode == PackingMode.SemiAutomatic && !_license.HasFeature(LicenseFeature.Camera)))
                throw new InvalidOperationException("The Job requires PLC and, in semi-automatic mode, Camera licenses.");
            var config = await GetAsync(ct); Validate(config);
            using (var uow = _factory.Create())
            {
                if (job.Mode == PackingMode.SemiAutomatic && await uow.Repository<Product>().Query().AnyAsync(x => x.JobId == job.Id && x.Code == config.NoReadToken, ct))
                    throw new InvalidOperationException("Camera no-read token conflicts with a Product code in this Job.");
            }
            if (config.RejectMode == PlcRejectMode.IndexedCommand && job.LastCameraSequence >= PlcCommandCodec.MaximumIndex)
                throw new InvalidOperationException("Camera sequence exhausted; create another Job.");
            // Connect all required endpoints before publishing any line command.
            if (!await _plc.ConnectAsync(config.PlcIp, config.PlcPort, TimeSpan.FromMilliseconds(config.TimeoutMs), ct))
                throw new InvalidOperationException("PLC connection failed.");
            if (job.Mode == PackingMode.SemiAutomatic)
            {
                // A fresh stream fences both incomplete and already-buffered pre-Start frames.
                await _camera.DisconnectAsync(ct);
                if (!await _camera.ConnectAsync(config.CameraIp, config.CameraPort, TimeSpan.FromMilliseconds(config.TimeoutMs), ct, config.CameraTerminator))
                    throw new InvalidOperationException("Camera connection failed.");
            }
            _config = config; _generation = _plc.Generation; _job = job; _stopping = false;
            try
            {
                await SetStateAsync(LineState.Hold, ct);
                if (job.Mode == PackingMode.SemiAutomatic && config.RejectMode == PlcRejectMode.IndexedCommand)
                    await SynchronizeAsync(job.LastCameraSequence, ct);
            }
            catch { _job = null; _config = null; throw; }
        }
        finally { _sessionGate.Release(); }
    }

    public Task SetStateAsync(LineState state, CancellationToken ct = default)
    {
        var config = _config;
        if (_job is null || config is null) return Task.CompletedTask;
        string command = state switch
        {
            LineState.Ready => config.ReadyCommand,
            LineState.Hold => config.HoldCommand,
            LineState.Full => config.FullCommand,
            LineState.Fault => config.FaultCommand,
            _ => config.StopCommand
        };
        return SendAsync(state.ToString(), command, null, ct);
    }

    public Task RejectAsync(long sequence, CancellationToken ct = default) => _config is null ? Task.CompletedTask :
        SendAsync("Reject", PlcCommandCodec.Reject(_config.RejectMode, sequence), sequence, ct);

    public Task SynchronizeAsync(long sequence, CancellationToken ct = default) =>
        _config?.RejectMode != PlcRejectMode.IndexedCommand ? Task.CompletedTask :
            SendAsync("Synchronize", PlcCommandCodec.Synchronize(sequence), sequence, ct);

    private async Task SendAsync(string kind, string payload, long? sequence, CancellationToken ct)
    {
        await _sendGate.WaitAsync(ct);
        try
        {
            var job = _job; var config = _config;
            if (job is null || config is null) return;
            using var uow = _factory.Create();
            var log = new LineEvent { JobId = job.Id, Kind = kind, Payload = payload, CameraSequence = sequence };
            uow.Repository<LineEvent>().Add(log);
            // Persist intent before touching the socket. Pending/Unknown are never replayed.
            await uow.SaveChangesAsync(ct);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(config.TimeoutMs);
                await _plc.SendAsync(payload, config.Framing, _generation, deadline.Token);
                log.Outcome = "SentToSocket"; log.SentAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                log.Outcome = ex is StalePlcConnectionException ? "NotSent" : "Unknown"; log.Error = ex.Message;
                await uow.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            await uow.SaveChangesAsync(CancellationToken.None);
        }
        finally { _sendGate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _sessionGate.WaitAsync(ct);
        try
        {
            _stopping = true;
            try { await SetStateAsync(LineState.Stopped, ct); }
            finally
            {
                bool disconnectCamera = _job?.Mode == PackingMode.SemiAutomatic;
                _job = null; _config = null;
                if (disconnectCamera) await _camera.DisconnectAsync(CancellationToken.None);
            }
        }
        finally { _sessionGate.Release(); }
    }

    private void OnFrame(object? sender, CameraFrameEventArgs frame)
    { if (_job?.Mode == PackingMode.SemiAutomatic && !_stopping) FrameReceived?.Invoke(this, frame); }
    private void OnCameraDisconnected(object? sender, CameraDisconnectedEventArgs args)
    { if (_job?.Mode == PackingMode.SemiAutomatic && !_stopping) Failed?.Invoke(this, new IOException("Camera disconnected: " + args.Reason)); }
    private void OnPlcState(object? sender, EventArgs args)
    { ConnectionStateChanged?.Invoke(this, EventArgs.Empty); if (_job is not null && !_stopping && (!_plc.IsConnected || _plc.Generation != _generation)) Failed?.Invoke(this, new IOException("PLC disconnected or replaced.")); }
    private void OnCameraState(object? sender, EventArgs args) => ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    private void OnPlcMessage(object? sender, PlcMessageEventArgs args)
    {
        if (_job?.Mode == PackingMode.SemiAutomatic && !_stopping && _config?.RejectMode == PlcRejectMode.IndexedCommand &&
            args.Generation == _generation && args.Message == PlcCommandCodec.SynchronizationRequest) SynchronizationRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _camera.FrameReceived -= OnFrame; _camera.Disconnected -= OnCameraDisconnected;
        _camera.ConnectionStateChanged -= OnCameraState;
        _plc.ConnectionStateChanged -= OnPlcState; _plc.MessageReceived -= OnPlcMessage;
        _sessionGate.Dispose(); _sendGate.Dispose();
    }
}
