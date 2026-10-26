using GH_Net.Repository.Abstractions;
using System.Net.Sockets;

namespace GH_Net.Repository.Devices;

/// <inheritdoc cref="ICameraDevice"/>
public sealed class CameraDevice : ICameraDevice
{
    private const int ReceiveBufferSize = 8 * 1024;

    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly CameraLineFrameDecoder _decoder = new();
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private long _generation;
    private bool _disposed;
    private string? _messageTerminator;

    public bool IsConnected { get; private set; }
    public string? CurrentIp { get; private set; }
    public int CurrentPort { get; private set; }

    public event EventHandler? ConnectionStateChanged;
    public event EventHandler<CameraFrameEventArgs>? FrameReceived;
    public event EventHandler<CameraDisconnectedEventArgs>? Disconnected;

    /// <inheritdoc/>
    public async Task<bool> ConnectAsync(
        string ip,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default, string? messageTerminator = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        if (messageTerminator is not (null or "CR" or "LF" or "CRLF"))
            throw new ArgumentException("Invalid camera terminator.", nameof(messageTerminator));
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
                CurrentPort == port && _messageTerminator == messageTerminator)
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
                _decoder.Reset();
                _decoder.SetTerminator(messageTerminator);
                _messageTerminator = messageTerminator;
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
        string? disconnectReason = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    disconnectReason = "Remote endpoint closed the connection.";
                    break;
                }

                IReadOnlyList<string> frames =
                    _decoder.Append(buffer.AsSpan(0, read));
                foreach (string frame in frames)
                {
                    if (generation != Volatile.Read(ref _generation) ||
                        ct.IsCancellationRequested)
                    {
                        return;
                    }

                    FrameReceived?.Invoke(
                        this,
                        new CameraFrameEventArgs
                        {
                            Value = frame,
                            ReceivedAt = DateTime.Now
                        });
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            disconnectReason = exception.Message;
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
                    _decoder.Reset();
                    notifyUnexpectedDisconnect = !ct.IsCancellationRequested;
                }
            }

            stream.Dispose();
            tcpClient.Dispose();

            if (notifyUnexpectedDisconnect)
            {
                ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                Disconnected?.Invoke(
                    this,
                    new CameraDisconnectedEventArgs { Reason = disconnectReason });
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
            _decoder.Reset();
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _connectionGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
