using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;

namespace Sessions.Infrastructure.Repositories;

public sealed class MetricsRepository(SessionsDbContext db) : IMetricsRepository
{
    public async Task<IReadOnlyList<FactTotal>> GetTotalsAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken)
    {
        // Distance with no stated effort counts at the default when one is applied, so range filters agree with the calculator.
        var fallback = query.DefaultEffort;
        var facts = db.SessionFacts.AsNoTracking().Where(f => f.OwnerId == userId);

        if (query.From is { } from) facts = facts.Where(f => f.PerformedOn >= from);
        if (query.To is { } to) facts = facts.Where(f => f.PerformedOn <= to);
        if (query.SessionId is { } sessionId) facts = facts.Where(f => f.SessionId == sessionId);
        if (query.Strokes is { Count: > 0 } strokes) facts = facts.Where(f => strokes.Contains(f.Stroke));
        if (query.Modes is { Count: > 0 } modes) facts = facts.Where(f => modes.Contains(f.Mode));
        if (query.MinPercent is { } min) facts = facts.Where(f => (f.Intensity ?? fallback) >= min);
        if (query.MaxPercent is { } max) facts = facts.Where(f => (f.Intensity ?? fallback) <= max);

        // Summed per session, stroke, mode and intensity: small however many sessions there are, and enough for the calculator to
        // group by anything (including sessions and time buckets) and count sessions.
        return await facts
            .GroupBy(f => new { f.PerformedOn, f.SessionId, f.Stroke, f.Mode, f.Intensity, f.Unit })
            .Select(g => new FactTotal(g.Key.PerformedOn, g.Key.SessionId, g.Key.Stroke, g.Key.Mode, g.Key.Intensity, g.Key.Unit, g.Sum(f => f.Distance)))
            .ToListAsync(cancellationToken);
    }

    public async Task<DateOnly?> FirstPerformedOnAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Sessions.AsNoTracking().Where(s => s.OwnerId == userId).MinAsync(s => (DateOnly?)s.PerformedOn, cancellationToken);
}
