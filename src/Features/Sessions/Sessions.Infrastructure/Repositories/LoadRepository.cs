using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Application.Load;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

public sealed class LoadRepository(SessionsDbContext db) : ILoadRepository
{
    public async Task<IReadOnlyList<SessionLoadRow>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        await db.Sessions
            .AsNoTracking()
            .Where(s => s.OwnerId == userId
                && (from == null || s.PerformedOn >= from)
                && (to == null || s.PerformedOn <= to))
            .Select(s => new SessionLoadRow(s.Id, s.PerformedOn, s.Load, s.UnratedDistance, s.Course == PoolCourse.Scy ? DistanceUnit.Yards : DistanceUnit.Metres))
            .ToListAsync(cancellationToken);
}
