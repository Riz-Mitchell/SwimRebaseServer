using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>
/// Turns opted-in, session-linked timed swims into the same shape <see cref="MetricsCalculator"/> sums over, so they count toward
/// training volume alongside ordinary session facts without a second write path into <see cref="SessionFact"/> (which would risk
/// being silently wiped whenever the linked session is next edited, since facts are fully deleted and reinserted per session).
/// A standalone swim (no <see cref="TimedSwim.SessionId"/>) never contributes, whatever the flag - there is no session for it to
/// be training volume of.
/// </summary>
public static class TimedSwimFacts
{
    public static IEnumerable<FactTotal> ToFactTotals(IEnumerable<TimedSwim> swims) =>
        swims
            .Where(s => s.IncludeInMetrics && s.SessionId is not null)
            .Select(s => new FactTotal(s.PerformedOn, s.SessionId!.Value, s.Stroke, s.Mode, s.Intensity, s.Unit, s.Dist));
}
