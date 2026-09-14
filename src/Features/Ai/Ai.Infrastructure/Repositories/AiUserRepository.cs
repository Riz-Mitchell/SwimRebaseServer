using Ai.Application.Interfaces;
using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Ai.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="AiDbContext"/> - no SaveChanges.</summary>
public sealed class AiUserRepository(AiDbContext db) : IAiUserRepository
{
    public Task<AiUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.AiUsers.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public void Add(AiUser user) => db.AiUsers.Add(user);

    public Task TouchLastActiveAtAsync(Guid userId, DateTimeOffset lastActiveAt, CancellationToken cancellationToken) =>
        db.AiUsers
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastActiveAt, lastActiveAt), cancellationToken);
}
