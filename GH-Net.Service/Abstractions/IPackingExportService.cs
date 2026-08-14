namespace GH_Net.Service.Abstractions;

public enum PackingExportFormat { Excel = 0, CsvZip = 1 }
public enum PackingExportScope { FullJob = 0, UnusedCodes = 1 }

[Flags]
public enum PackingCodeSelection
{
    None = 0,
    Products = 1,
    Cartons = 2,
    Pallets = 4,
    All = Products | Cartons | Pallets
}

public sealed record PackingExportRequest(
    PackingExportFormat Format,
    PackingExportScope Scope = PackingExportScope.FullJob,
    PackingCodeSelection CodeSelection = PackingCodeSelection.All);

public sealed record PackingExportResult(int Products, int Cartons, int Pallets, int ScanEvents, int PrintAttempts);
public interface IPackingExportService
{
    Task<PackingExportResult> ExportAsync(int jobId, PackingExportRequest request, Stream destination, CancellationToken ct = default);
}
