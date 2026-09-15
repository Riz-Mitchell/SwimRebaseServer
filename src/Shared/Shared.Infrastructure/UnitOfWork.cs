using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shared.Application;

namespace Shared.Infrastructure;

/// <summary>
/// Generic EF Core <see cref="IUnitOfWork"/> closed over one feature's DbContext. Register a keyed
/// instance per feature (<c>services.AddKeyedScoped&lt;IUnitOfWork, UnitOfWork&lt;TContext&gt;&gt;(key)</c>)
/// so multiple features can share this implementation without colliding on the unkeyed registration.
/// </summary>
public sealed class UnitOfWork<TContext>(TContext db) : IUnitOfWork
    where TContext : DbContext
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return new EfUnitOfWorkTransaction(transaction);
    }

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
