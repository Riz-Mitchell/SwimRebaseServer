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
            var (total, grouped) = await AskAsync(userId, definition, from, to, cancellationToken: cancellationToken);

            values.Add(new CustomMetricValue(
                metric.Id, definition.Name, definition.Measure, definition.Window,
                ValueOf(total, definition.Measure), grouped?.Rows, total.Units, total.AssumedDistance));
        }

        return values;
    }

    /// <summary>
    /// One saved metric over the period around <paramref name="request"/>'s date, optionally in another window or grouping. Nothing is saved.
    /// </summary>
    /// <returns>Null when the swimmer has no such metric.</returns>
    /// <exception cref="InvalidCustomMetricException">The series would have too many bars.</exception>
    /// <exception cref="InvalidMetricsQueryException">The requested effort scale is unknown.</exception>
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

        // Bands only mean something for a distance series; otherwise the request for them is ignored.
        var banded = request.ThenBy is not null && definition.GroupBy is not null && definition.Measure == MetricMeasure.Distance;
        var (total, grouped) = await AskAsync(userId, definition, from, end, banded ? request : null, cancellationToken);
        return new CustomMetricDetail(
            metric.Id, definition.Name, definition.Measure, definition.Window, new DateRangeView(start, end),
            ValueOf(total, definition.Measure), grouped?.Rows, total.Units, total.AssumedDistance,
            banded ? grouped?.Scale : null, banded ? grouped?.DefaultEffort : null);
    }

    private async Task<(MetricsResult Total, MetricsResult? Grouped)> AskAsync(
        Guid userId, CustomMetricDefinition definition, DateOnly? from, DateOnly? to, CustomMetricDetailRequest? bands = null, CancellationToken cancellationToken = default)
    {
        var total = await answers.QueryAsync(userId, CustomMetricQuery.For(definition, from, to), cancellationToken);
        var grouped = definition.GroupBy is { } dimension
            ? await answers.QueryAsync(userId, CustomMetricQuery.For(definition, from, to, dimension, bands?.ThenBy, bands?.ScaleId), cancellationToken)
            : null;
        return (total, grouped);
    }

    /// <summary>The first session's date, or <paramref name="end"/> when there is none or it is after <paramref name="end"/>.</summary>
    private async Task<DateOnly> FirstSwamAsync(Guid userId, DateOnly end, CancellationToken cancellationToken) =>
        await facts.FirstPerformedOnAsync(userId, cancellationToken) is { } first && first <= end ? first : end;

    // An ungrouped query over nothing has no row at all.
    private static double? ValueOf(MetricsResult total, MetricMeasure measure) =>
        total.Rows.FirstOrDefault()?.Value ?? (measure == MetricMeasure.AvgIntensity ? (double?)null : 0);
}
