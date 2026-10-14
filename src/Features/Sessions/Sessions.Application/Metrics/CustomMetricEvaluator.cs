using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>Answers a swimmer's saved metrics through the same <see cref="MetricsService"/> every other question goes through.</summary>
public sealed class CustomMetricEvaluator(ICustomMetricRepository metrics, IMetricsRepository facts, MetricsService answers)
{
    public async Task<IReadOnlyList<CustomMetricValue>> EvaluateAsync(Guid userId, DateOnly today, CancellationToken cancellationToken)
    {
        var values = new List<CustomMetricValue>();
        foreach (var metric in await metrics.ListAsync(userId, cancellationToken))
        {
            var definition = metric.ToDefinition();
            var (from, to) = MetricWindows.Resolve(definition.Window, today);
            var (total, series) = await AskAsync(userId, definition, from, to, cancellationToken);

            values.Add(new CustomMetricValue(
                metric.Id, definition.Name, definition.Measure, definition.Window,
                ValueOf(total, definition.Measure), series, total.Units, total.AssumedDistance));
        }

        return values;
    }

    /// <summary>
    /// One saved metric over the period around <paramref name="request"/>'s date, optionally in another window or grouping. Nothing is saved.
    /// </summary>
    /// <returns>Null when the swimmer has no such metric.</returns>
    /// <exception cref="InvalidCustomMetricException">The series would have too many bars.</exception>
    public async Task<CustomMetricDetail?> EvaluateOneAsync(Guid userId, Guid id, CustomMetricDetailRequest request, CancellationToken cancellationToken)
    {
        if (await metrics.FindAsync(userId, id, cancellationToken) is not { } metric)
        {
            return null;
        }

        var definition = metric.ToDefinition() with
        {
            Window = request.Window ?? metric.Window,
            GroupBy = request.GroupBy ?? metric.GroupBy,
        };
        var (from, to) = MetricWindows.Period(definition.Window, request.Today);
        var end = to ?? request.Today;

        // All time reads up to the anchor, so stepping back in time shows what was true then.
        var start = from ?? await FirstSwamAsync(userId, end, cancellationToken);
        CustomMetricDetailRequest.EnsureChartable(definition.GroupBy, start, end);

        var (total, series) = await AskAsync(userId, definition, from, end, cancellationToken);
        return new CustomMetricDetail(
            metric.Id, definition.Name, definition.Measure, definition.Window, new DateRangeView(start, end),
            ValueOf(total, definition.Measure), series, total.Units, total.AssumedDistance);
    }

    private async Task<(MetricsResult Total, IReadOnlyList<MetricRow>? Series)> AskAsync(
        Guid userId, CustomMetricDefinition definition, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var total = await answers.QueryAsync(userId, CustomMetricQuery.For(definition, from, to), cancellationToken);
        var series = definition.GroupBy is { } dimension
            ? (await answers.QueryAsync(userId, CustomMetricQuery.For(definition, from, to, dimension), cancellationToken)).Rows
            : null;
        return (total, series);
    }

    /// <summary>The first session's date, or <paramref name="end"/> when there is none or it is after <paramref name="end"/>.</summary>
    private async Task<DateOnly> FirstSwamAsync(Guid userId, DateOnly end, CancellationToken cancellationToken) =>
        await facts.FirstPerformedOnAsync(userId, cancellationToken) is { } first && first <= end ? first : end;

    // An ungrouped query over nothing has no row at all.
    private static double? ValueOf(MetricsResult total, MetricMeasure measure) =>
        total.Rows.FirstOrDefault()?.Value ?? (measure == MetricMeasure.AvgIntensity ? (double?)null : 0);
}
