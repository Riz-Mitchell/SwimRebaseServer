using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;

namespace Sessions.Infrastructure.Repositories;

public sealed class MetricsRepository(SessionsDbContext db) : IMetricsRepository
{
    public async Task<IReadOnlyList<FactTotal>> GetTotalsAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken)
    {
        var facts = db.SessionFacts.AsNoTracking().Where(f => f.OwnerId == userId);

        if (query.From is { } from) facts = facts.Where(f => f.PerformedOn >= from);
        if (query.To is { } to) facts = facts.Where(f => f.PerformedOn <= to);
        if (query.Strokes is { Count: > 0 } strokes) facts = facts.Where(f => strokes.Contains(f.Stroke));
        if (query.Modes is { Count: > 0 } modes) facts = facts.Where(f => modes.Contains(f.Mode));
        if (query.MinIntensity is { } min) facts = facts.Where(f => f.Intensity >= min);
        if (query.MaxIntensity is { } max) facts = facts.Where(f => f.Intensity <= max);

        // Summed per session, stroke, mode and intensity: small however many sessions there are, and enough for the calculator to
        // group by anything (including sessions and time buckets) and count sessions.
        return await facts
            .GroupBy(f => new { f.PerformedOn, f.SessionId, f.Stroke, f.Mode, f.Intensity, f.Unit })
            .Select(g => new FactTotal(g.Key.PerformedOn, g.Key.SessionId, g.Key.Stroke, g.Key.Mode, g.Key.Intensity, g.Key.Unit, g.Sum(f => (long)f.Distance)))
            .ToListAsync(cancellationToken);
    }
}
