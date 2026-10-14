using Sessions.Application.Metrics;

namespace Sessions.Application.Interfaces;

public interface IMetricsRepository
{
    /// <summary>
    /// The user's facts that match the query's filters (dates, strokes, modes, intensity bounds), summed per session, stroke, mode and
    /// intensity. Grouping, roll-ups and measures are left to <see cref="MetricsCalculator"/>.
    /// </summary>
    Task<IReadOnlyList<FactTotal>> GetTotalsAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken);

    /// <summary>The date of the user's earliest saved session; null when they have none.</summary>
    Task<DateOnly?> FirstPerformedOnAsync(Guid userId, CancellationToken cancellationToken);
}
