using GH_Net.Repository.Abstractions;
using System.Net.Sockets;
using System.Text;

namespace GH_Net.Repository.Devices;

public sealed class ZebraPrinterDevice : IZebraPrinterDevice
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _disposed;

    public bool IsConnected => _client?.Connected == true && _stream is not null;
    public string? CurrentIp { get; private set; }
    public int CurrentPort { get; private set; }
    public event EventHandler? ConnectionStateChanged;

    public async Task<bool> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DisconnectCore();
            var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await client.ConnectAsync(ip, port, timeoutCts.Token).ConfigureAwait(false);
                _client = client; _stream = client.GetStream(); CurrentIp = ip; CurrentPort = port;
                RaiseConnectionStateChanged();
                return true;
            }
            catch
            {
                client.Dispose(); DisconnectCore();
                ct.ThrowIfCancellationRequested();
                RaiseConnectionStateChanged(); return false;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SendZplAsync(string zpl, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zpl)) throw new ArgumentException("ZPL is required.", nameof(zpl));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsConnected || _stream is null) throw new InvalidOperationException("Zebra printer is not connected.");
            byte[] bytes = Encoding.UTF8.GetBytes(zpl);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await _stream.WriteAsync(bytes, timeoutCts.Token).ConfigureAwait(false);
                await _stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch
            {
                DisconnectCore(); RaiseConnectionStateChanged(); throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { bool changed = _client is not null; DisconnectCore(); if (changed) RaiseConnectionStateChanged(); }
        finally { _gate.Release(); }
    }

    private void DisconnectCore()
    {
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null; _client = null; CurrentIp = null; CurrentPort = 0;
    }

    private void RaiseConnectionStateChanged()
    {
        foreach (EventHandler handler in ConnectionStateChanged?.GetInvocationList() ?? []) try { handler(this, EventArgs.Empty); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true; _gate.Dispose();
    }
}
