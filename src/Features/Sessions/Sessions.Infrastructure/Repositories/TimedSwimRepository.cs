using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionsDbContext"/> - no SaveChanges.</summary>
public sealed class TimedSwimRepository(SessionsDbContext db) : ITimedSwimRepository
{
    public void Add(TimedSwim timedSwim) => db.TimedSwims.Add(timedSwim);

    public async Task<TimedSwim?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.TimedSwims.AsNoTracking().Include(t => t.Resistances).FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == userId, cancellationToken);

    public async Task<TimedSwim?> FindForUpdateAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.TimedSwims.Include(t => t.Resistances).FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == userId, cancellationToken);

    public void ReplaceResistances(TimedSwim timedSwim, IReadOnlyList<TimedSwimResistance> replacements)
    {
        // Explicit remove and add, so each new row is inserted rather than guessed at from its key.
        db.TimedSwimResistances.RemoveRange(timedSwim.Resistances);
        timedSwim.Resistances = [];
        db.TimedSwimResistances.AddRange(replacements);
        timedSwim.Resistances = [.. replacements];
    }

    public void Remove(TimedSwim timedSwim) => db.TimedSwims.Remove(timedSwim);

    public async Task<IReadOnlyList<TimedSwim>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken)
    {
        var query = db.TimedSwims.AsNoTracking().Include(t => t.Resistances).Where(t => t.OwnerId == userId);

        if (filter.SessionId is { } sessionId) query = query.Where(t => t.SessionId == sessionId);
        if (filter.From is { } from) query = query.Where(t => t.PerformedOn >= from);
        if (filter.To is { } to) query = query.Where(t => t.PerformedOn <= to);
        if (filter.Distances is { Count: > 0 } distances) query = query.Where(t => distances.Contains(t.Dist));
        if (filter.Strokes is { Count: > 0 } strokes) query = query.Where(t => strokes.Contains(t.Stroke));
        if (filter.MinPercent is { } min) query = query.Where(t => t.Intensity >= min);
        if (filter.MaxPercent is { } max) query = query.Where(t => t.Intensity <= max);
        if (filter.StartTypes is { Count: > 0 } startTypes) query = query.Where(t => t.StartType != null && startTypes.Contains(t.StartType.Value));
        if (filter.ResistanceKinds is { Count: > 0 } kinds) query = query.Where(t => t.Resistances.Any(r => kinds.Contains(r.Kind)));
        if (filter.HasResistance is { } hasResistance) query = query.Where(t => t.Resistances.Any() == hasResistance);

        return await query.OrderBy(t => t.Seconds).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TimedSwimFact>> GetFactsAsync(Guid userId, TimedSwimMetricsQuery query, CancellationToken cancellationToken)
    {
        var swims = db.TimedSwims.AsNoTracking().Where(t => t.OwnerId == userId);

        if (query.From is { } from) swims = swims.Where(t => t.PerformedOn >= from);
        if (query.To is { } to) swims = swims.Where(t => t.PerformedOn <= to);
        if (query.Distances is { Count: > 0 } distances) swims = swims.Where(t => distances.Contains(t.Dist));
        if (query.Strokes is { Count: > 0 } strokes) swims = swims.Where(t => strokes.Contains(t.Stroke));
        if (query.Modes is { Count: > 0 } modes) swims = swims.Where(t => modes.Contains(t.Mode));
        if (query.Courses is { Count: > 0 } courses) swims = swims.Where(t => courses.Contains(t.Course));
        if (query.StartTypes is { Count: > 0 } startTypes) swims = swims.Where(t => t.StartType != null && startTypes.Contains(t.StartType.Value));
        if (query.Finishes is { Count: > 0 } finishes) swims = swims.Where(t => t.Finish != null && finishes.Contains(t.Finish.Value));
        if (query.ResistanceKinds is { Count: > 0 } kinds) swims = swims.Where(t => t.Resistances.Any(r => kinds.Contains(r.Kind)));
        if (query.HasResistance is { } hasResistance) swims = swims.Where(t => t.Resistances.Any() == hasResistance);
        if (query.MinPercent is { } min) swims = swims.Where(t => t.Intensity >= min);
        if (query.MaxPercent is { } max) swims = swims.Where(t => t.Intensity <= max);

        var rows = await swims
            .Select(t => new { t.PerformedOn, t.Course, t.Dist, t.Stroke, t.Mode, t.Seconds, t.Intensity, t.StartType, t.Finish, Kinds = t.Resistances.Select(r => r.Kind).ToList() })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new TimedSwimFact(r.PerformedOn, r.Course, r.Dist, r.Stroke, r.Mode, r.Seconds, r.Intensity, r.StartType, r.Finish, r.Kinds)).ToList();
    }
}
