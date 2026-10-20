using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

public sealed record CustomMetricView(Guid Id, int Position, CustomMetricDefinition Definition)
{
    public static CustomMetricView From(CustomMetric metric) => new(metric.Id, metric.Position, metric.ToDefinition());
}

/// <param name="Value">Null when the measure does not apply (an average intensity over no distance).</param>
/// <param name="Series">The metric by day, week or month, in order; periods with nothing swum are absent. Null when the metric asks for none.</param>
/// <param name="AssumedDistance">How much of the distance had no stated effort and was counted at the swimmer's default effort.</param>
public sealed record CustomMetricValue(
    Guid Id,
    string Name,
    MetricMeasure Measure,
    MetricWindow Window,
    double? Value,
    IReadOnlyList<MetricRow>? Series,
    IReadOnlyList<DistanceUnit> Units,
    decimal AssumedDistance);

/// <summary>The dates a window covered, inclusive. A week or month that has not finished ends after today.</summary>
public sealed record DateRangeView(DateOnly From, DateOnly To);

/// <summary>One metric over a chosen period: a <see cref="CustomMetricValue"/> plus the dates it covered.</summary>
/// <param name="Scale">The effort scale a banded series' second key belongs to; null when the series is not banded.</param>
/// <param name="DefaultEffort">The percent distance with no stated effort was counted at in a banded series; null when the series is not banded.</param>
public sealed record CustomMetricDetail(
    Guid Id,
    string Name,
    MetricMeasure Measure,
    MetricWindow Window,
    DateRangeView Range,
    double? Value,
    IReadOnlyList<MetricRow>? Series,
    IReadOnlyList<DistanceUnit> Units,
    decimal AssumedDistance,
    EffortScale? Scale = null,
    int? DefaultEffort = null);

public sealed class InvalidCustomMetricException(string message) : Exception(message);

public static class CustomMetricQuery
{
    public static MetricsQuery For(
        CustomMetricDefinition definition, DateOnly? from, DateOnly? to, MetricDimension? groupBy = null, MetricDimension? thenBy = null, string? scaleId = null) => new(
        from,
        to,
        definition.Strokes,
        definition.Modes,
        definition.MinPercent,
        definition.MaxPercent,
        new[] { groupBy, thenBy }.OfType<MetricDimension>().ToList(),
        definition.Measure,
        scaleId);
}
