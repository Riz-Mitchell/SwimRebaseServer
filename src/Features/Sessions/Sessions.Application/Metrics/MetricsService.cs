using Sessions.Application.Interfaces;

namespace Sessions.Application.Metrics;

public sealed class MetricsService(IMetricsRepository metrics)
{
    /// <exception cref="InvalidMetricsQueryException">The query cannot be answered as asked.</exception>
    public async Task<MetricsResult> QueryAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken)
    {
        // Compute once up front so a bad query is rejected before any database work.
        MetricsCalculator.Compute(query, []);
        var totals = await metrics.GetTotalsAsync(userId, query, cancellationToken);
        return MetricsCalculator.Compute(query, totals);
    }
}
