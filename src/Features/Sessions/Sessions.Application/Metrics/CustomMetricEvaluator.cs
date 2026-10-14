using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>Answers each of a swimmer's saved metrics through the same <see cref="MetricsService"/> every other question goes through.</summary>
public sealed class CustomMetricEvaluator(ICustomMetricRepository metrics, MetricsService answers)
{
    public async Task<IReadOnlyList<CustomMetricValue>> EvaluateAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var values = new List<CustomMetricValue>();
        foreach (var metric in await metrics.ListAsync(userId, cancellationToken))
        {
            var definition = metric.ToDefinition();
            var (from, to) = MetricWindows.Resolve(definition.Window, today);

            var total = await answers.QueryAsync(userId, CustomMetricQuery.For(definition, from, to), cancellationToken);
            var series = definition.GroupBy is { } dimension
                ? (await answers.QueryAsync(userId, CustomMetricQuery.For(definition, from, to, dimension), cancellationToken)).Rows
                : null;

            values.Add(new CustomMetricValue(
                metric.Id, definition.Name, definition.Measure, definition.Window,
                total.Rows.FirstOrDefault()?.Value ?? Empty(definition.Measure),
                series, total.Units, total.AssumedDistance));
        }

        return values;
    }

    // An ungrouped query over nothing has no row at all.
    private static double? Empty(MetricMeasure measure) => measure == MetricMeasure.AvgIntensity ? null : 0;
}
