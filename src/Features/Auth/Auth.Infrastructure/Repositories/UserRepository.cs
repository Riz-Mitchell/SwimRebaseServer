using Auth.Application.Interfaces;
using Auth.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="AuthDbContext"/> - no SaveChanges.</summary>
public sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    public Task<AppUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public void Add(AppUser user) => db.Users.Add(user);

    public Task TouchLastSeenAtAsync(Guid userId, DateTimeOffset lastSeenAt, CancellationToken cancellationToken) =>
        db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastSeenAt, lastSeenAt), cancellationToken);
}
