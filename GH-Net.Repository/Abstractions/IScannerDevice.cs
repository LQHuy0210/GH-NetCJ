using GH_Net.Repository.Entities;

namespace GH_Net.Repository.Abstractions;

public sealed class ScannerCodeEventArgs : EventArgs
{
    public required string Code { get; init; }
    public DateTime ReceivedAt { get; init; } = DateTime.Now;
}

public interface IScannerDevice : IAsyncDisposable
{
    bool IsConnected { get; }
    string? CurrentPortName { get; }
    event EventHandler<ScannerCodeEventArgs>? CodeReceived;
    event EventHandler? ConnectionStateChanged;
    event EventHandler<Exception>? ReadFailed;
    IReadOnlyList<string> GetAvailablePortNames();
    Task<bool> ConnectAsync(ScannerConfig config, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
