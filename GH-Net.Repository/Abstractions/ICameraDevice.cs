namespace GH_Net.Repository.Abstractions;

/// <summary>A completed raw frame received from the camera TCP stream.</summary>
public sealed class CameraFrameEventArgs : EventArgs
{
    /// <summary>Exact frame after transport terminator removal.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Local receive timestamp.</summary>
    public DateTime ReceivedAt { get; init; } = DateTime.Now;
}

/// <summary>Details for an unexpected camera TCP disconnection.</summary>
public sealed class CameraDisconnectedEventArgs : EventArgs
{
    public string? Reason { get; init; }
}

/// <summary>
/// Long-lived TCP client for the single global camera/Visual Station.
/// </summary>
public interface ICameraDevice : IAsyncDisposable
{
    bool IsConnected { get; }
    string? CurrentIp { get; }
    int CurrentPort { get; }

    event EventHandler? ConnectionStateChanged;
    event EventHandler<CameraFrameEventArgs>? FrameReceived;
    event EventHandler<CameraDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Connects once using the supplied timeout. Changing endpoint replaces the
    /// old socket before the new connection is attempted.
    /// </summary>
    Task<bool> ConnectAsync(
        string ip,
        int port,
        TimeSpan timeout,
        CancellationToken ct = default, string? messageTerminator = null);

    /// <summary>Disconnects safely and clears incomplete frame data.</summary>
    Task DisconnectAsync(CancellationToken ct = default);
}
