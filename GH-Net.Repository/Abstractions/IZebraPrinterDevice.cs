namespace GH_Net.Repository.Abstractions;

public interface IZebraPrinterDevice : IAsyncDisposable
{
    bool IsConnected { get; }
    string? CurrentIp { get; }
    int CurrentPort { get; }
    event EventHandler? ConnectionStateChanged;
    Task<bool> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendZplAsync(string zpl, TimeSpan timeout, CancellationToken ct = default);
}
