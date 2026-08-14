namespace GH_Net.Repository.Abstractions;

/// <summary>
/// Creates an isolated Unit of Work for background operations.
/// Each caller owns and must dispose the returned instance.
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>Creates a Unit of Work backed by a new DbContext.</summary>
    IUnitOfWork Create();
}
