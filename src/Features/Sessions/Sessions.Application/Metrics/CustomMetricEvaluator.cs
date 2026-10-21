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

        // Bands only mean something for a distance series; otherwise the request for them is ignored.
        var banded = request.ThenBy is not null && definition.GroupBy is not null && definition.Measure == MetricMeasure.Distance;
        var bands = banded ? request : null;
        var first = await facts.FirstPerformedOnAsync(userId, cancellationToken);

        var current = await PeriodAsync(userId, definition, request.Today, bands, first, cancellationToken);
        CustomMetricPeriod? previous = null;
        if (request.IncludePrevious && MetricWindows.PreviousAnchor(definition.Window, request.Today) is { } before)
        {
            previous = (await PeriodAsync(userId, definition, before, bands, first, cancellationToken)).View;
        }

        return new CustomMetricDetail(
            metric.Id, definition.Name, definition.Measure, definition.Window, current.View.Range,
            current.View.Value, current.View.Series, current.View.Units, current.View.AssumedDistance,
            banded ? current.Grouped?.Scale : null, banded ? current.Grouped?.DefaultEffort : null, first, previous);
    }

    private sealed record PeriodResult(CustomMetricPeriod View, MetricsResult? Grouped);

    private async Task<PeriodResult> PeriodAsync(
        Guid userId, CustomMetricDefinition definition, DateOnly anchor, CustomMetricDetailRequest? bands, DateOnly? firstSession, CancellationToken cancellationToken)
    {
        var (from, to) = MetricWindows.Period(definition.Window, anchor);
        var end = to ?? anchor;

        // All time reads up to the anchor, so stepping back in time shows what was true then.
        var start = from ?? (firstSession is { } first && first <= end ? first : end);
        CustomMetricDetailRequest.EnsureChartable(definition.GroupBy, start, end);

        var (total, grouped) = await AskAsync(userId, definition, from, end, bands, cancellationToken);
        return new PeriodResult(
            new CustomMetricPeriod(new DateRangeView(start, end), ValueOf(total, definition.Measure), grouped?.Rows, total.Units, total.AssumedDistance),
            grouped);
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

    // An ungrouped query over nothing has no row at all.
    private static double? ValueOf(MetricsResult total, MetricMeasure measure) =>
        total.Rows.FirstOrDefault()?.Value ?? (measure == MetricMeasure.AvgIntensity ? (double?)null : 0);
}
