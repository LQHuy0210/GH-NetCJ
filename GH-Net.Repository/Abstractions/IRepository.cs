using GH_Net.Repository.Paginate;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace GH_Net.Repository.Abstractions;

/// <summary>
/// Generic repository — interface duy nhất để Service layer truy cập data.
/// Tất cả I/O đều qua UnitOfWork.SaveChangesAsync() — không tự SaveChanges trong repo.
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    // ── Raw Query (nâng cao) ──────────────────────────────────────────────────

    /// <summary>Trả về IQueryable thô — service layer tự compose filter/order/include.</summary>
    IQueryable<TEntity> Query();

    /// <summary>Tìm theo primary key. Trả null nếu không thấy.</summary>
    ValueTask<TEntity?> FindAsync(int id, CancellationToken ct = default);

    // ── Get Single ────────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy 1 entity đầu tiên theo điều kiện. Trả null nếu không tìm thấy.
    /// </summary>
    Task<TEntity?> SingleOrDefaultAsync(
        Expression<Func<TEntity, bool>>?                             predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?       orderBy   = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        CancellationToken ct = default);

    // ── Get List ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy toàn bộ danh sách theo điều kiện (không phân trang).
    /// Chỉ dùng khi chắc chắn tập kết quả nhỏ.
    /// </summary>
    Task<IReadOnlyList<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>>?                             predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?       orderBy   = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        CancellationToken ct = default);

    // ── Paging ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Phân trang — trả về IPaginate với metadata (Total, TotalPages, HasNext…).
    /// Dùng cho JobsView (danh sách job), PrintHistoryView (lịch sử in dài).
    /// </summary>
    /// <param name="page">Trang hiện tại, bắt đầu từ 1.</param>
    /// <param name="size">Số item/trang. Mặc định 20.</param>
    Task<IPaginate<TEntity>> GetPagingAsync(
        Expression<Func<TEntity, bool>>?                             predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?       orderBy   = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        int page = 1,
        int size = 20,
        CancellationToken ct = default);

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Thêm 1 entity vào ChangeTracker (chưa commit).</summary>
    TEntity Add(TEntity entity);

    /// <summary>Thêm nhiều entity cùng lúc (bulk insert, chưa commit).</summary>
    void AddRange(IEnumerable<TEntity> entities);

    /// <summary>Đánh dấu entity để xóa (chưa commit).</summary>
    void Remove(TEntity entity);

    /// <summary>Đánh dấu nhiều entity để xóa (chưa commit).</summary>
    void RemoveRange(IEnumerable<TEntity> entities);
}
