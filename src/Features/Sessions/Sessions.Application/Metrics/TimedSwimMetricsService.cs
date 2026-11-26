using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Interfaces;

namespace Sessions.Application.Metrics;

/// <summary>Answers timed-swim metric questions. Independent of <see cref="MetricsService"/>: timed swims never feed session volume.</summary>
public sealed class TimedSwimMetricsService(ITimedSwimRepository timedSwims, ISessionPreferencesRepository preferences, ILogger<TimedSwimMetricsService>? logger = null)
{
    private readonly ILogger<TimedSwimMetricsService> _logger = logger ?? NullLogger<TimedSwimMetricsService>.Instance;

    /// <exception cref="InvalidMetricsQueryException">The query cannot be answered as asked.</exception>
    public async Task<TimedSwimMetricsResult> QueryAsync(Guid userId, TimedSwimMetricsQuery query, CancellationToken cancellationToken)
    {
        // Compute once up front so a bad query is rejected before any database work.
        TimedSwimMetricsCalculator.Compute(query, []);

        var saved = await preferences.FindAsync(userId, cancellationToken);
        var resolved = query with { Scale = EffortScaleChoice.Resolve(query.ScaleId, saved) };
        var facts = await timedSwims.GetFactsAsync(userId, resolved, cancellationToken);
        _logger.LogDebug("[{ClassName}] Timed-swim metrics query for user {UserId} over {Count} swim(s), measure {Measure}", nameof(TimedSwimMetricsService), userId, facts.Count, query.Measure);
        return TimedSwimMetricsCalculator.Compute(resolved, facts);
    }
}
