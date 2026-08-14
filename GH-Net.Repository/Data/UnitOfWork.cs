using GH_Net.Repository.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace GH_Net.Repository.Data;

/// <summary>
/// Triển khai IUnitOfWork — bọc PrinterManagerDbContext.
/// Cache các repository theo Type để tái sử dụng trong cùng transaction.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly PrinterManagerDbContext _db;
    private readonly Dictionary<Type, object> _repos = new();
    private SqliteTransaction? _deferredReadTransaction;
    private bool _disposed;

    public UnitOfWork(PrinterManagerDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public IRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        var type = typeof(TEntity);
        if (!_repos.TryGetValue(type, out var repo))
        {
            repo = new EfRepository<TEntity>(_db);
            _repos[type] = repo;
        }
        return (IRepository<TEntity>)repo;
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    /// <inheritdoc/>
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => _db.Database.BeginTransactionAsync(ct);

    /// <inheritdoc/>
    public async Task<IDbContextTransaction> BeginReadTransactionAsync(
        CancellationToken ct = default)
    {
        if (_deferredReadTransaction is not null || _db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("This unit of work already has an active transaction.");

        await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = (SqliteConnection)_db.Database.GetDbConnection();
            _deferredReadTransaction = connection.BeginTransaction(deferred: true);
            return await _db.Database.UseTransactionAsync(_deferredReadTransaction, ct)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Unable to attach the SQLite read transaction.");
        }
        catch
        {
            await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public DbConnection GetDbConnection() => _db.Database.GetDbConnection();

    /// <inheritdoc/>
    public DbTransaction? GetDbTransaction()
        => _db.Database.CurrentTransaction?.GetDbTransaction();

    /// <inheritdoc/>
    public void Detach<TEntity>(TEntity entity) where TEntity : class
        => _db.Entry(entity).State = EntityState.Detached;

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _deferredReadTransaction?.Dispose();
            _db.Dispose();
            _repos.Clear();
        }
        _disposed = true;
    }
}
