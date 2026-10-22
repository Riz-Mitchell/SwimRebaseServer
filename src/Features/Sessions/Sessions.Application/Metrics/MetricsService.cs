using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

public sealed class MetricsService(IMetricsRepository metrics, ISessionPreferencesRepository preferences, ILogger<MetricsService>? logger = null)
{
    private readonly ILogger<MetricsService> _logger = logger ?? NullLogger<MetricsService>.Instance;

    /// <exception cref="InvalidMetricsQueryException">The query cannot be answered as asked.</exception>
    public async Task<MetricsResult> QueryAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken)
    {
        // Compute once up front so a bad query is rejected before any database work.
        MetricsCalculator.Compute(query, []);

        var saved = await preferences.FindAsync(userId, cancellationToken);
        var resolved = query with
        {
            Scale = EffortScaleChoice.Resolve(query.ScaleId, saved),
            DefaultEffort = query.ApplyDefaultEffort ? saved?.DefaultEffort ?? SessionPreferences.BuiltInDefaultEffort : null,
        };
        var totals = await metrics.GetTotalsAsync(userId, resolved, cancellationToken);
        _logger.LogDebug("[{ClassName}] Metrics query for user {UserId} over {RowCount} total row(s), measure {Measure}", nameof(MetricsService), userId, totals.Count, query.Measure);
        return MetricsCalculator.Compute(resolved, totals);
    }
}
