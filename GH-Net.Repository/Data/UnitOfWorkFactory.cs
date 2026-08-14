using GH_Net.Repository.Abstractions;

namespace GH_Net.Repository.Data;

/// <inheritdoc cref="IUnitOfWorkFactory"/>
public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly string _connectionString;

    /// <summary>Creates a factory for the application database.</summary>
    public UnitOfWorkFactory()
        : this(PrinterManagerDbContext.DefaultConnectionString)
    {
    }

    /// <summary>Creates a factory for a specific database connection string.</summary>
    public UnitOfWorkFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public IUnitOfWork Create()
        => new UnitOfWork(PrinterManagerDbContext.Create(_connectionString));
}
