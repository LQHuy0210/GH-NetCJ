using GH_Net.Repository.Entities;

namespace GH_Net.Service.Abstractions;

public sealed record PackingCsvSelection(string FilePath, string CodeColumn);

public sealed class CreatePackingJobRequest
{
    public string? Name { get; init; }
    public bool UseAutomaticName { get; init; } = true;
    public DateTime? AutomaticNameTimestamp { get; init; }
    public int ProductsPerCarton { get; init; }
    public GH_Net.Core.Enums.PackingMode Mode { get; init; }
    public bool UsePlc { get; init; }
    public int CartonsPerPallet { get; init; }
    public required PackingCsvSelection Products { get; init; }
    public required PackingCsvSelection Cartons { get; init; }
    public required PackingCsvSelection Pallets { get; init; }
}

public interface IPackingJobService
{
    Task<Job> CreateAsync(CreatePackingJobRequest request, CancellationToken ct = default);
}
