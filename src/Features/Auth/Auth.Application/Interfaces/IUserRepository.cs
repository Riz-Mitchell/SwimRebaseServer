using Auth.Domain.Models;

namespace Auth.Application.Interfaces;

/// <summary>Pure query/tracking against the auth store - no SaveChanges. See <see cref="Shared.Infrastructure.IUnitOfWork"/>.</summary>
public interface IUserRepository
{
    Task<AppUser?> FindAsync(Guid userId, CancellationToken cancellationToken);

    void Add(AppUser user);

    /// <summary>Immediate bulk update, bypassing the change tracker - no separate SaveChanges needed.</summary>
    Task TouchLastSeenAtAsync(Guid userId, DateTimeOffset lastSeenAt, CancellationToken cancellationToken);
}
