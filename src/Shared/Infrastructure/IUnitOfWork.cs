namespace Shared.Infrastructure;

/// <summary>
/// Owns SaveChanges and transaction boundaries. Repositories only track/query - they never call
/// SaveChanges or open a transaction themselves; callers in an Application layer decide when a unit
/// of work commits. Each feature registers its own keyed <see cref="UnitOfWork{TContext}"/> closure
/// over its own DbContext (see each feature's ServiceCollectionExtensions) - consumers inject this
/// interface with a <c>[FromKeyedServices]</c> attribute using that feature's key.
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
