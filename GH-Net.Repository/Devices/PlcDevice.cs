using GH_Net.Repository.Abstractions;
using System.Net.Sockets;
using System.Text;
using GH_Net.Core.Enums;

namespace GH_Net.Repository.Devices;

/// <inheritdoc cref="IPlcDevice"/>
public sealed class PlcDevice : IPlcDevice
{
    private const int ReceiveBufferSize = 8 * 1024;

    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private long _generation;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public string? CurrentIp { get; private set; }
    public int CurrentPort { get; private set; }

    public event EventHandler? ConnectionStateChanged;
    public event EventHandler<PlcMessageEventArgs>? MessageReceived;
    public long Generation => Volatile.Read(ref _generation);


    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(
        string ip,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsConnected &&
                string.Equals(CurrentIp, ip, StringComparison.OrdinalIgnoreCase) &&
                CurrentPort == port)
            {
                return true;
            }

            await DisconnectCoreAsync().ConfigureAwait(false);
            long generation = Interlocked.Increment(ref _generation);
            var tcpClient = new TcpClient();
            TcpConnectionOptions.Apply(tcpClient);

            try
            {
                using var timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);
                await tcpClient
                    .ConnectAsync(ip, port, timeoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                tcpClient.Dispose();
                return false;
            }
            catch (SocketException)
            {
                tcpClient.Dispose();
                return false;
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }

            var receiveCts = new CancellationTokenSource();
            NetworkStream stream = tcpClient.GetStream();
            lock (_stateSync)
            {
                _tcpClient = tcpClient;
                _stream = stream;
                _receiveCts = receiveCts;

                IsConnected = true;
                CurrentIp = ip;
                CurrentPort = port;
            }

            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            _receiveTask = Task.Run(
                () => ReceiveLoopAsync(
                    tcpClient,
                    stream,
                    generation,
                    receiveCts.Token),
                CancellationToken.None);
            return true;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(
        TcpClient tcpClient,
        NetworkStream stream,
        long generation,
        CancellationToken ct)
    {
        var buffer = new byte[ReceiveBufferSize];
        var decoder = new PlcMessageDecoder();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                foreach (string message in decoder.Feed(buffer.AsSpan(0, read)))
                {
                    if (generation != Generation) break;
                    foreach (EventHandler<PlcMessageEventArgs> handler in MessageReceived?.GetInvocationList() ?? [])
                        try { handler(this, new PlcMessageEventArgs(message, generation)); }
                        catch { /* Observers cannot terminate the receive loop. */ }
                }

            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _ = exception;
        }
        finally
        {
            bool notifyUnexpectedDisconnect = false;
            lock (_stateSync)
            {
                if (generation == Volatile.Read(ref _generation) &&
                    ReferenceEquals(_tcpClient, tcpClient))
                {
                    IsConnected = false;
                    CurrentIp = null;
                    CurrentPort = 0;
                    _tcpClient = null;
                    _stream = null;
                    _receiveCts = null;
                    _receiveTask = null;

                    notifyUnexpectedDisconnect = !ct.IsCancellationRequested;
                }
            }

            stream.Dispose();
            tcpClient.Dispose();

            if (notifyUnexpectedDisconnect)
            {
                ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task DisconnectCoreAsync()
    {
        Interlocked.Increment(ref _generation);

        CancellationTokenSource? receiveCts;
        Task? receiveTask;
        TcpClient? tcpClient;
        NetworkStream? stream;
        bool stateChanged;

        lock (_stateSync)
        {
            receiveCts = _receiveCts;
            receiveTask = _receiveTask;
            tcpClient = _tcpClient;
            stream = _stream;
            stateChanged = IsConnected;

            IsConnected = false;
            CurrentIp = null;
            CurrentPort = 0;
            _receiveCts = null;
            _receiveTask = null;
            _tcpClient = null;
            _stream = null;

        }

        receiveCts?.Cancel();
        stream?.Dispose();
        tcpClient?.Dispose();
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // Cancellation/socket disposal is expected during replacement.
            }
        }

        receiveCts?.Dispose();
        if (stateChanged)
        {
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public async Task SendAsync(string payload, PlcFraming framing, long generation, CancellationToken ct = default)
    {
        string message = framing switch
        {
            PlcFraming.CR => payload + "\r", PlcFraming.LF => payload + "\n",
            PlcFraming.CRLF => payload + "\r\n", PlcFraming.STX_ETX => "\u0002" + payload + "\u0003",
            _ => throw new ArgumentOutOfRangeException(nameof(framing))
        };
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            NetworkStream stream;
            lock (_stateSync)
            {
                if (!IsConnected || generation != Generation || _stream is null)
                    throw new StalePlcConnectionException();
                stream = _stream;
            }
            try { await stream.WriteAsync(Encoding.ASCII.GetBytes(message), ct).ConfigureAwait(false); }
            catch
            {
                // A failed write on an old socket must not disconnect a newly restored socket.
                await _connectionGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    bool stillCurrent;
                    lock (_stateSync)
                        stillCurrent = generation == Generation && ReferenceEquals(stream, _stream);
                    if (stillCurrent) await DisconnectCoreAsync().ConfigureAwait(false);
                }
                finally { _connectionGate.Release(); }
                throw;
            }
        }
        finally { _sendGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _connectionGate.Dispose();
        _sendGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
