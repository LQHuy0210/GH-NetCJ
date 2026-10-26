using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;

namespace GH_Net.Service.Abstractions;

public enum LineState { Ready, Hold, Full, Fault, Stopped }

public interface IAutomationService
{
    event EventHandler<CameraFrameEventArgs>? FrameReceived;
    event EventHandler<Exception>? Failed;
    event EventHandler? SynchronizationRequested;
    event EventHandler? ConnectionStateChanged;
    bool CameraConnected { get; }
    bool PlcConnected { get; }
    Task<AutomationConfig> GetAsync(CancellationToken ct = default);
    Task ApplyAsync(AutomationConfig config, CancellationToken ct = default);
    Task ApplyCameraAsync(AutomationConfig config, CancellationToken ct = default);
    Task ApplyPlcAsync(AutomationConfig config, CancellationToken ct = default);
    Task ApplyPlcConnectionAsync(AutomationConfig config, CancellationToken ct = default);
    Task StartAsync(Job job, CancellationToken ct = default);
    Task SetStateAsync(LineState state, CancellationToken ct = default);
    Task RejectAsync(long sequence, CancellationToken ct = default);
    Task SynchronizeAsync(long sequence, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
