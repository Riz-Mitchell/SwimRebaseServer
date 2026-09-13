namespace Ai.Application.Interfaces;

/// <summary>
/// Owns SaveChanges and transaction boundaries. Repositories only track/query - they never call
/// SaveChanges or open a transaction themselves; callers in Ai.Application decide when a unit of
/// work commits.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}

/// <summary>An open transaction boundary. Dispose without calling <see cref="CommitAsync"/> to roll back.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
