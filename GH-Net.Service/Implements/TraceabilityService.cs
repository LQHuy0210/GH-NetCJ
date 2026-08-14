using GH_Net.Core.Enums;
using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GH_Net.Service.Implements;

public sealed class TraceabilityService(IUnitOfWorkFactory factory) : ITraceabilityService
{
    public async Task<IReadOnlyList<TraceabilityResult>> SearchExactAsync(string code, CancellationToken ct = default)
    {
        code = code?.Trim() ?? string.Empty;
        if (code.Length == 0) return [];
        using var uow = factory.Create();
        await using var tx = await uow.BeginReadTransactionAsync(ct);
        var products = await uow.Repository<Product>().Query().AsNoTracking().Where(x => x.Code == code).ToListAsync(ct);
        var cartons = await uow.Repository<Carton>().Query().AsNoTracking().Where(x => x.Code == code).ToListAsync(ct);
        var pallets = await uow.Repository<Pallet>().Query().AsNoTracking().Where(x => x.Code == code).ToListAsync(ct);
        var jobIds = products.Select(x => x.JobId).Concat(cartons.Select(x => x.JobId)).Concat(pallets.Select(x => x.JobId)).Distinct().ToArray();
        if (jobIds.Length == 0) { await tx.CommitAsync(ct); return []; }
        var jobs = await uow.Repository<Job>().Query().AsNoTracking().Where(x => jobIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var allProducts = await uow.Repository<Product>().Query().AsNoTracking().Where(x => jobIds.Contains(x.JobId)).ToListAsync(ct);
        var allCartons = await uow.Repository<Carton>().Query().AsNoTracking().Where(x => jobIds.Contains(x.JobId)).ToListAsync(ct);
        var allPallets = await uow.Repository<Pallet>().Query().AsNoTracking().Where(x => jobIds.Contains(x.JobId)).ToListAsync(ct);
        var results = new List<TraceabilityResult>();
        foreach (var product in products)
        {
            var carton = product.CartonId is int cid ? allCartons.SingleOrDefault(x => x.Id == cid) : null;
            var pallet = carton?.PalletId is int pid ? allPallets.SingleOrDefault(x => x.Id == pid) : null;
            results.Add(new(product.JobId, jobs[product.JobId].Name, ScanCodeType.Product, product.Code,
                pallet is null ? null : MapPallet(pallet, allCartons, allProducts), carton is null ? null : MapCarton(carton, allProducts), MapProduct(product)));
        }
        foreach (var carton in cartons)
        {
            var pallet = carton.PalletId is int pid ? allPallets.SingleOrDefault(x => x.Id == pid) : null;
            results.Add(new(carton.JobId, jobs[carton.JobId].Name, ScanCodeType.Carton, carton.Code,
                pallet is null ? null : MapPallet(pallet, allCartons, allProducts), MapCarton(carton, allProducts), null));
        }
        foreach (var pallet in pallets)
            results.Add(new(pallet.JobId, jobs[pallet.JobId].Name, ScanCodeType.Pallet, pallet.Code, MapPallet(pallet, allCartons, allProducts), null, null));
        await tx.CommitAsync(ct);
        return results.OrderByDescending(x => x.JobId).ToList();
    }

    private static TraceProduct MapProduct(Product x) => new(x.Code, x.Status, x.ScannedAt, x.PackedAt);
    private static TraceCarton MapCarton(Carton x, IReadOnlyList<Product> products) => new(x.Code, x.Status, x.ProductCount, x.PrintedAt, x.CompletedAt, products.Where(p => p.CartonId == x.Id).OrderBy(p => p.SourceRowIndex).Select(MapProduct).ToList());
    private static TracePallet MapPallet(Pallet x, IReadOnlyList<Carton> cartons, IReadOnlyList<Product> products) => new(x.Code, x.Status, x.CartonCount, x.PrintedAt, x.CompletedAt, cartons.Where(c => c.PalletId == x.Id).OrderBy(c => c.SourceRowIndex).Select(c => MapCarton(c, products)).ToList());
}
