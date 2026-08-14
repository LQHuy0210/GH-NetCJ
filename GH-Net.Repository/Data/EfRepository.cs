using GH_Net.Repository.Abstractions;
using GH_Net.Repository.Paginate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace GH_Net.Repository.Data;

/// <summary>
/// Triển khai IRepository trên EF Core DbContext.
/// Không tự gọi SaveChanges — UnitOfWork quản lý việc commit.
/// </summary>
public class EfRepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    private readonly PrinterManagerDbContext _db;
    private readonly DbSet<TEntity> _set;

    public EfRepository(PrinterManagerDbContext db)
    {
        _db  = db  ?? throw new ArgumentNullException(nameof(db));
        _set = _db.Set<TEntity>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Áp dụng predicate + include + orderBy vào IQueryable.</summary>
    private IQueryable<TEntity> BuildQuery(
        Expression<Func<TEntity, bool>>? predicate,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include)
    {
        IQueryable<TEntity> q = _set.AsQueryable();
        if (include   != null) q = include(q);
        if (predicate != null) q = q.Where(predicate);
        if (orderBy   != null) q = orderBy(q);
        return q;
    }

    // ── Raw ───────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IQueryable<TEntity> Query() => _set.AsQueryable();

    /// <inheritdoc/>
    public ValueTask<TEntity?> FindAsync(int id, CancellationToken ct = default)
        => _set.FindAsync([id], ct);

    // ── Get Single ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TEntity?> SingleOrDefaultAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        CancellationToken ct = default)
        => BuildQuery(predicate, orderBy, include).FirstOrDefaultAsync(ct);

    // ── Get List ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        CancellationToken ct = default)
    {
        var list = await BuildQuery(predicate, orderBy, include).ToListAsync(ct);
        return list.AsReadOnly();
    }

    // ── Paging ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<IPaginate<TEntity>> GetPagingAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        int page = 1,
        int size = 20,
        CancellationToken ct = default)
        => BuildQuery(predicate, orderBy, include).ToPaginateAsync(page, size, ct);

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public TEntity Add(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _set.Add(entity);
        return entity;
    }

    /// <inheritdoc/>
    public void AddRange(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _set.AddRange(entities);
    }

    /// <inheritdoc/>
    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _set.Remove(entity);
    }

    /// <inheritdoc/>
    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _set.RemoveRange(entities);
    }
}
