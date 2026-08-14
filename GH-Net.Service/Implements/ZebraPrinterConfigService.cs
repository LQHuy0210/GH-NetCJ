using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;

namespace GH_Net.Service.Implements;

public sealed class ZebraPrinterConfigService : IZebraPrinterConfigService
{
    private readonly IUnitOfWork _uow;
    private readonly IZebraPrinterDevice _printer;
    private readonly IZplTemplateRenderer _renderer;

    public ZebraPrinterConfigService(IUnitOfWork uow, IZebraPrinterDevice printer, IZplTemplateRenderer renderer)
    {
        _uow = uow; _printer = printer; _renderer = renderer;
    }

    public Task<PrinterConfig?> GetAsync(CancellationToken ct = default)
        => _uow.Repository<PrinterConfig>().Query().AsNoTracking()
            .OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<ZebraPrinterApplyResult> ApplyAsync(PrinterConfig config, CancellationToken ct = default)
    {
        Validate(config);
        await EnsureNoRunningJobAsync(ct).ConfigureAwait(false);
        var repository = _uow.Repository<PrinterConfig>();
        PrinterConfig? existing = await repository.Query().OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing is null)
        {
            config.CreatedAt = config.UpdatedAt = DateTime.Now; repository.Add(config); existing = config;
        }
        else
        {
            existing.Ip = config.Ip; existing.Port = config.Port;
            existing.CartonZplTemplate = config.CartonZplTemplate;
            existing.PalletZplTemplate = config.PalletZplTemplate;
            existing.ConnectTimeoutMs = config.ConnectTimeoutMs; existing.SendTimeoutMs = config.SendTimeoutMs;
            existing.UpdatedAt = DateTime.Now;
        }
        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        bool connected = await _printer.ConnectAsync(existing.Ip, existing.Port,
            TimeSpan.FromMilliseconds(existing.ConnectTimeoutMs), ct).ConfigureAwait(false);
        if (connected)
        {
            existing.LastConnectedAt = DateTime.Now;
            await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return new ZebraPrinterApplyResult(existing, connected);
    }

    public async Task<bool> ConnectSavedAsync(CancellationToken ct = default)
    {
        PrinterConfig? config = await GetAsync(ct).ConfigureAwait(false);
        return config is not null && await _printer.ConnectAsync(config.Ip, config.Port,
            TimeSpan.FromMilliseconds(config.ConnectTimeoutMs), ct).ConfigureAwait(false);
    }

    public async Task TestPrintAsync(bool cartonTemplate, CancellationToken ct = default)
    {
        await EnsureNoRunningJobAsync(ct).ConfigureAwait(false);
        PrinterConfig config = await GetAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Zebra printer settings are missing.");
        if (!_printer.IsConnected || !string.Equals(_printer.CurrentIp, config.Ip, StringComparison.OrdinalIgnoreCase) || _printer.CurrentPort != config.Port)
            throw new InvalidOperationException("Apply and connect Zebra printer settings before test print.");
        string template = cartonTemplate ? config.CartonZplTemplate! : config.PalletZplTemplate!;
        string code = cartonTemplate ? "TEST-CARTON" : "TEST-PALLET";
        string zpl = _renderer.Render(template, code);
        await _printer.SendZplAsync(zpl, TimeSpan.FromMilliseconds(config.SendTimeoutMs), ct).ConfigureAwait(false);
    }

    private void Validate(PrinterConfig config)
    {
        if (!IPAddress.TryParse(config.Ip, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("Zebra printer IPv4 address is invalid.");
        if (config.Port is < 1 or > 65535) throw new InvalidOperationException("Zebra printer port is invalid.");
        if (config.ConnectTimeoutMs <= 0 || config.SendTimeoutMs <= 0) throw new InvalidOperationException("Zebra printer timeouts must be positive.");
        _renderer.Validate(config.CartonZplTemplate ?? string.Empty);
        _renderer.Validate(config.PalletZplTemplate ?? string.Empty);
    }

    private async Task EnsureNoRunningJobAsync(CancellationToken ct)
    {
        if (await _uow.Repository<Job>().Query().AsNoTracking().AnyAsync(job => job.Status == JobStatus.Running, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Stop the running Job before changing Zebra printer settings.");
    }
}
