using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionsDbContext"/> - no SaveChanges.</summary>
public sealed class TimedSwimRepository(SessionsDbContext db) : ITimedSwimRepository
{
    public void Add(TimedSwim timedSwim) => db.TimedSwims.Add(timedSwim);

    public async Task<TimedSwim?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.TimedSwims.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == userId, cancellationToken);

    public async Task<IReadOnlyList<TimedSwim>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken)
    {
        var query = db.TimedSwims.AsNoTracking().Where(t => t.OwnerId == userId);

        if (filter.SessionId is { } sessionId) query = query.Where(t => t.SessionId == sessionId);
        if (filter.From is { } from) query = query.Where(t => t.PerformedOn >= from);
        if (filter.To is { } to) query = query.Where(t => t.PerformedOn <= to);
        if (filter.Distances is { Count: > 0 } distances) query = query.Where(t => distances.Contains(t.Dist));
        if (filter.Strokes is { Count: > 0 } strokes) query = query.Where(t => strokes.Contains(t.Stroke));
        if (filter.MinPercent is { } min) query = query.Where(t => t.Intensity >= min);
        if (filter.MaxPercent is { } max) query = query.Where(t => t.Intensity <= max);
        if (filter.StartTypes is { Count: > 0 } startTypes) query = query.Where(t => t.StartType != null && startTypes.Contains(t.StartType.Value));
        if (filter.ResistanceTypes is { Count: > 0 } resistanceTypes) query = query.Where(t => t.ResistanceType != null && resistanceTypes.Contains(t.ResistanceType));
        if (filter.HasResistance is { } hasResistance) query = query.Where(t => (t.ResistanceType != null) == hasResistance);

        return await query.OrderBy(t => t.Seconds).ToListAsync(cancellationToken);
    }
}
