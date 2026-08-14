using Microsoft.EntityFrameworkCore;

namespace GH_Net.Repository.Paginate;

/// <summary>Extension methods phân trang cho IQueryable — dùng EF Core CountAsync + Skip/Take.</summary>
public static class PaginateExtension
{
    /// <summary>
    /// Phân trang async từ IQueryable đã được filter/order.
    /// </summary>
    /// <param name="source">IQueryable đã filter và order.</param>
    /// <param name="page">Trang hiện tại (bắt đầu từ 1).</param>
    /// <param name="size">Số item mỗi trang.</param>
    /// <param name="ct">CancellationToken.</param>
    public static async Task<IPaginate<T>> ToPaginateAsync<T>(
        this IQueryable<T> source,
        int page,
        int size,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (size < 1) size = 1;

        var total = await source.CountAsync(ct);
        var items = await source
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new Paginate<T>(items, total, page, size);
    }

    /// <summary>
    /// Phân trang async với projection — tránh load toàn bộ entity khi chỉ cần ViewModel.
    /// </summary>
    public static async Task<IPaginate<TResult>> ToPaginateAsync<T, TResult>(
        this IQueryable<T> source,
        Func<IQueryable<T>, IQueryable<TResult>> selector,
        int page,
        int size,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (size < 1) size = 1;

        var total = await source.CountAsync(ct);
        var projected = selector(source.Skip((page - 1) * size).Take(size));
        var items = await projected.ToListAsync(ct);

        return new Paginate<TResult>(items, total, page, size);
    }
}
