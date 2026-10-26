using GH_Net.Core.Enums;
using GH_Net.Core.Packing;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;

namespace GH_Net.Service.Implements;

public sealed class PackingJobService : IPackingJobService
{
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ICsvImportService _csv;

    public PackingJobService(IUnitOfWorkFactory unitOfWorkFactory, ICsvImportService csv)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _csv = csv;
    }

    public async Task<Job> CreateAsync(CreatePackingJobRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackingPolicy.ValidateMode(request.Mode, request.UsePlc);
        PackingPolicy.ValidateCapacity(request.ProductsPerCarton, nameof(request.ProductsPerCarton));
        PackingPolicy.ValidateCapacity(request.CartonsPerPallet, nameof(request.CartonsPerPallet));
        if (!request.UseAutomaticName && string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Job name is required.");

        IReadOnlyList<ImportedCode> products = await _csv.ReadCodeColumnAsync(
            request.Products.FilePath, request.Products.CodeColumn, ct).ConfigureAwait(false);
        IReadOnlyList<ImportedCode> cartons = await _csv.ReadCodeColumnAsync(
            request.Cartons.FilePath, request.Cartons.CodeColumn, ct).ConfigureAwait(false);
        IReadOnlyList<ImportedCode> pallets = await _csv.ReadCodeColumnAsync(
            request.Pallets.FilePath, request.Pallets.CodeColumn, ct).ConfigureAwait(false);

        _ = PackingPolicy.BuildFullPackingPlan(
            products.Count,
            request.ProductsPerCarton,
            cartons.Count,
            request.CartonsPerPallet,
            pallets.Count);

        using IUnitOfWork uow = _unitOfWorkFactory.Create();
        await using var transaction = await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            DateTime createdAt = DateTime.Now;
            var job = new Job
            {
                Name = request.UseAutomaticName ? string.Empty : request.Name!.Trim(),
                Status = JobStatus.NewlyCreated,
                PackingWorkflowState = PackingWorkflowState.Stopped,
                ProductsPerCarton = request.ProductsPerCarton,
                CartonsPerPallet = request.CartonsPerPallet,
                ProductCsvFileName = Path.GetFileName(request.Products.FilePath),
                ProductCodeColumn = request.Products.CodeColumn,
                CartonCsvFileName = Path.GetFileName(request.Cartons.FilePath),
                CartonCodeColumn = request.Cartons.CodeColumn,
                PalletCsvFileName = Path.GetFileName(request.Pallets.FilePath),
                PalletCodeColumn = request.Pallets.CodeColumn,
                TotalData = products.Count,
                Mode = request.Mode,
                UsePlc = request.UsePlc,
                CreatedAt = createdAt
            };
            uow.Repository<Job>().Add(job);
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            if (request.UseAutomaticName)
                job.Name = $"{(request.AutomaticNameTimestamp ?? createdAt):yyMMdd_HHmmss}_Job{job.Id}";

            uow.Repository<Product>().AddRange(products.Select(item => new Product
            {
                JobId = job.Id, Code = item.Code, SourceRowIndex = item.SourceRowIndex
            }));
            uow.Repository<Carton>().AddRange(cartons.Select(item => new Carton
            {
                JobId = job.Id, Code = item.Code, SourceRowIndex = item.SourceRowIndex, Sequence = item.SourceRowIndex + 1
            }));
            uow.Repository<Pallet>().AddRange(pallets.Select(item => new Pallet
            {
                JobId = job.Id, Code = item.Code, SourceRowIndex = item.SourceRowIndex, Sequence = item.SourceRowIndex + 1
            }));
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return job;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
