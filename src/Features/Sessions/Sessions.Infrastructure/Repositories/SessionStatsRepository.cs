using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Reads and adjusts <see cref="SessionsDbContext"/>'s per-user session count. Adjusting is immediate SQL, not tracked.</summary>
public sealed class SessionStatsRepository(SessionsDbContext db) : ISessionStatsRepository
{
    public async Task AdjustAsync(Guid userId, int delta, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One statement, so concurrent creates and deletes for the same user cannot lose an update. A delete for a user with no row
        // inserts 0, never a negative count.
        var initial = Math.Max(delta, 0);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO session_stats (owner_id, session_count, updated_at)
            VALUES ({userId}, {initial}, {now})
            ON CONFLICT (owner_id) DO UPDATE
            SET session_count = GREATEST(session_stats.session_count + {delta}, 0), updated_at = {now}
            """,
            cancellationToken);
    }

    public async Task<int> GetCountAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.SessionStats
            .AsNoTracking()
            .Where(s => s.OwnerId == userId)
            .Select(s => s.SessionCount)
            .FirstOrDefaultAsync(cancellationToken);
}
