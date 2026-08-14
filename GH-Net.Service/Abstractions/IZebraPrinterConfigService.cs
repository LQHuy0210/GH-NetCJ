using GH_Net.Repository.Entities;

namespace GH_Net.Service.Abstractions;

public interface IZplTemplateRenderer
{
    string Render(string template, string code);
    void Validate(string template);
}

public sealed record ZebraPrinterApplyResult(PrinterConfig Config, bool Connected);

public interface IZebraPrinterConfigService
{
    Task<PrinterConfig?> GetAsync(CancellationToken ct = default);
    Task<ZebraPrinterApplyResult> ApplyAsync(PrinterConfig config, CancellationToken ct = default);
    Task<bool> ConnectSavedAsync(CancellationToken ct = default);
    Task TestPrintAsync(bool cartonTemplate, CancellationToken ct = default);
}
