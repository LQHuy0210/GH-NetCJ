using GH_Net.Core.Enums;

namespace GH_Net.Service.Abstractions;

public sealed record TraceProduct(string Code, ProductStatus Status, DateTime? ScannedAt, DateTime? PackedAt);
public sealed record TraceCarton(string Code, CartonStatus Status, int ProductCount, DateTime? PrintedAt, DateTime? CompletedAt, IReadOnlyList<TraceProduct> Products);
public sealed record TracePallet(string Code, PalletStatus Status, int CartonCount, DateTime? PrintedAt, DateTime? CompletedAt, IReadOnlyList<TraceCarton> Cartons);
public sealed record TraceabilityResult(int JobId, string JobName, ScanCodeType MatchedType, string MatchedCode, TracePallet? Pallet, TraceCarton? Carton, TraceProduct? Product);

public interface ITraceabilityService
{
    Task<IReadOnlyList<TraceabilityResult>> SearchExactAsync(string code, CancellationToken ct = default);
}
