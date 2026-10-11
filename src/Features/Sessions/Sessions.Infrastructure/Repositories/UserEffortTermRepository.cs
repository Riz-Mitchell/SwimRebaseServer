using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionsDbContext"/> - no SaveChanges.</summary>
public sealed class UserEffortTermRepository(SessionsDbContext db) : IUserEffortTermRepository
{
    public async Task<IReadOnlyList<UserEffortTerm>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserEffortTerms.AsNoTracking().Where(t => t.OwnerId == userId).ToListAsync(cancellationToken);

    public async Task<UserEffortTerm?> FindAsync(Guid userId, string phrase, CancellationToken cancellationToken) =>
        await db.UserEffortTerms.FirstOrDefaultAsync(t => t.OwnerId == userId && t.Phrase == phrase, cancellationToken);

    public void Add(UserEffortTerm term) => db.UserEffortTerms.Add(term);
}
