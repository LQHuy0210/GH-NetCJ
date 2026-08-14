namespace GH_Net.Repository.Abstractions;

using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

/// <summary>
/// Unit of Work — quản lý một DbContext duy nhất cho toàn bộ transaction.
/// Service layer dùng UoW để:
///   1. Lấy repository cho từng entity.
///   2. Thực hiện nhiều thao tác (Add/Remove/Update).
///   3. Gọi SaveChangesAsync() 1 lần để commit tất cả.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>Lấy repository cho entity TEntity. Trả cùng instance nếu đã tạo trước đó.</summary>
    IRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>Commit tất cả thay đổi đang pending xuống SQLite.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Bắt đầu database transaction cho một chuỗi thao tác nguyên tử.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts a deferred read transaction. This preserves one SQLite snapshot while
    /// allowing runtime writers to continue when the database uses WAL mode.
    /// </summary>
    Task<IDbContextTransaction> BeginReadTransactionAsync(CancellationToken ct = default);

    /// <summary>Returns the connection owned by this unit of work for high-volume prepared commands.</summary>
    DbConnection GetDbConnection();

    /// <summary>Returns the active database transaction, if one has been started.</summary>
    DbTransaction? GetDbTransaction();

    /// <summary>Ngừng tracking entity sau khi đã lưu để giới hạn RAM ở luồng dài hạn.</summary>
    void Detach<TEntity>(TEntity entity) where TEntity : class;
}
