using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionsDbContext"/> - no SaveChanges.</summary>
public sealed class SessionPreferencesRepository(SessionsDbContext db) : ISessionPreferencesRepository
{
    public async Task<SessionPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.SessionPreferences.FirstOrDefaultAsync(p => p.OwnerId == userId, cancellationToken);

    public void Add(SessionPreferences preferences) => db.SessionPreferences.Add(preferences);
}
