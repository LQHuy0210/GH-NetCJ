using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using System.IO.Ports;

namespace GH_Net.Repository.Devices;

public sealed class SerialScannerDevice : IScannerDevice
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SerialCodeFrameDecoder _decoder = new();
    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private string _terminator = "CR";
    private bool _disposed;

    public bool IsConnected => _port?.IsOpen == true;
    public string? CurrentPortName => _port?.PortName;
    public event EventHandler<ScannerCodeEventArgs>? CodeReceived;
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler<Exception>? ReadFailed;

    public IReadOnlyList<string> GetAvailablePortNames()
        => SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<bool> ConnectAsync(ScannerConfig config, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(config);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            var port = new SerialPort(
                config.PortName,
                config.BaudRate,
                (Parity)config.Parity,
                config.DataBits,
                (StopBits)config.StopBits)
            {
                Encoding = System.Text.Encoding.UTF8,
                ReadTimeout = config.ReadTimeoutMs,
                WriteTimeout = config.ReadTimeoutMs
            };
            try
            {
                ct.ThrowIfCancellationRequested();
                port.Open();
                _port = port;
                _terminator = config.MessageTerminator;
                _decoder.Reset();
                _readCts = new CancellationTokenSource();
                _readTask = ReadLoopAsync(port, _readCts.Token);
                RaiseConnectionStateChanged();
                return true;
            }
            catch
            {
                port.Dispose();
                ct.ThrowIfCancellationRequested();
                RaiseConnectionStateChanged();
                return false;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await DisconnectCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task ReadLoopAsync(SerialPort port, CancellationToken ct)
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (!ct.IsCancellationRequested && port.IsOpen)
            {
                int count = await port.BaseStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (count == 0) continue;
                foreach (string code in _decoder.Feed(buffer.AsSpan(0, count), _terminator))
                    RaiseCodeReceived(code);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            try { port.Close(); } catch { }
            RaiseReadFailed(exception);
            RaiseConnectionStateChanged();
        }
    }

    private async Task DisconnectCoreAsync()
    {
        CancellationTokenSource? cts = _readCts;
        Task? readTask = _readTask;
        SerialPort? port = _port;
        _readCts = null; _readTask = null; _port = null;
        if (cts is not null) cts.Cancel();
        if (port is not null)
        {
            try { port.Close(); } catch { }
        }
        if (readTask is not null)
        {
            try { await readTask.ConfigureAwait(false); } catch { }
        }
        cts?.Dispose(); port?.Dispose(); _decoder.Reset();
        if (port is not null) RaiseConnectionStateChanged();
    }

    private void RaiseCodeReceived(string code)
    {
        foreach (EventHandler<ScannerCodeEventArgs> handler in CodeReceived?.GetInvocationList() ?? [])
            try { handler(this, new ScannerCodeEventArgs { Code = code }); } catch { }
    }

    private void RaiseConnectionStateChanged()
    {
        foreach (EventHandler handler in ConnectionStateChanged?.GetInvocationList() ?? []) try { handler(this, EventArgs.Empty); } catch { }
    }

    private void RaiseReadFailed(Exception exception)
    {
        foreach (EventHandler<Exception> handler in ReadFailed?.GetInvocationList() ?? []) try { handler(this, exception); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true; _gate.Dispose();
    }
}
