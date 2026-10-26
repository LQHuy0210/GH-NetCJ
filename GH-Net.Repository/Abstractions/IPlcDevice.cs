using GH_Net.Core.Enums;
namespace GH_Net.Repository.Abstractions;

/// <summary>Singleton TCP transport; generation fences prevent stale signals.</summary>
public interface IPlcDevice : IAsyncDisposable
{
    bool IsConnected { get; }
    string? CurrentIp { get; }
    int CurrentPort { get; }
    long Generation { get; }
    event EventHandler? ConnectionStateChanged;
    event EventHandler<PlcMessageEventArgs>? MessageReceived;
    Task<bool> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendAsync(string payload, PlcFraming framing, long generation, CancellationToken ct = default);
}

/// <summary>A complete PLC reply associated with its original TCP connection.</summary>
public sealed class PlcMessageEventArgs(string message, long generation) : EventArgs
{
    public string Message { get; } = message;
    public long Generation { get; } = generation;
}

/// <summary>The socket changed before any bytes were written.</summary>
public sealed class StalePlcConnectionException : InvalidOperationException
{
    public StalePlcConnectionException() : base("PLC connection changed before sending.") { }
}
