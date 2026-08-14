using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GH_Net.Service.Implements;

public sealed class ScannerConfigService : IScannerConfigService
{
    private readonly IUnitOfWork _uow;
    private readonly IScannerDevice _scanner;

    public ScannerConfigService(IUnitOfWork uow, IScannerDevice scanner)
    {
        _uow = uow; _scanner = scanner;
    }

    public Task<ScannerConfig?> GetAsync(CancellationToken ct = default)
        => _uow.Repository<ScannerConfig>().Query().AsNoTracking()
            .OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(ct);

    public IReadOnlyList<string> GetAvailablePortNames() => _scanner.GetAvailablePortNames();

    public async Task<ScannerApplyResult> ApplyAsync(ScannerConfig config, CancellationToken ct = default)
    {
        Validate(config);
        await EnsureNoRunningJobAsync(ct).ConfigureAwait(false);
        var repository = _uow.Repository<ScannerConfig>();
        ScannerConfig? existing = await repository.Query().OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing is null)
        {
            config.CreatedAt = config.UpdatedAt = DateTime.Now; repository.Add(config); existing = config;
        }
        else
        {
            existing.PortName = config.PortName; existing.BaudRate = config.BaudRate;
            existing.DataBits = config.DataBits; existing.Parity = config.Parity;
            existing.StopBits = config.StopBits; existing.MessageTerminator = config.MessageTerminator;
            existing.ReadTimeoutMs = config.ReadTimeoutMs; existing.Enabled = config.Enabled;
            existing.UpdatedAt = DateTime.Now;
        }
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        bool connected = await _scanner.ConnectAsync(existing, ct).ConfigureAwait(false);
        if (connected)
        {
            existing.LastConnectedAt = DateTime.Now;
            await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return new ScannerApplyResult(existing, connected);
    }

    public async Task<bool> ConnectSavedAsync(CancellationToken ct = default)
    {
        ScannerConfig? config = await GetAsync(ct).ConfigureAwait(false);
        return config is not null && config.Enabled && await _scanner.ConnectAsync(config, ct).ConfigureAwait(false);
    }

    private static void Validate(ScannerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.PortName)) throw new InvalidOperationException("Scanner COM port is required.");
        if (config.BaudRate <= 0) throw new InvalidOperationException("Scanner baud rate must be positive.");
        if (config.DataBits is < 5 or > 8) throw new InvalidOperationException("Scanner data bits must be between 5 and 8.");
        if (config.Parity is < 0 or > 4) throw new InvalidOperationException("Scanner parity is invalid.");
        if (config.StopBits is < 1 or > 3) throw new InvalidOperationException("Scanner stop bits are invalid.");
        if (config.MessageTerminator is not ("CR" or "LF" or "CRLF")) throw new InvalidOperationException("Scanner terminator is invalid.");
        if (config.ReadTimeoutMs <= 0) throw new InvalidOperationException("Scanner timeout must be positive.");
    }

    private async Task EnsureNoRunningJobAsync(CancellationToken ct)
    {
        if (await _uow.Repository<Job>().Query().AsNoTracking().AnyAsync(job => job.Status == JobStatus.Running, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Stop the running Job before changing scanner settings.");
    }
}
