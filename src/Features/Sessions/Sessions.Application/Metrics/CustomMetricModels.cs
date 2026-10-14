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

public sealed class InvalidCustomMetricException(string message) : Exception(message);

public static class CustomMetricQuery
{
    public static MetricsQuery For(CustomMetricDefinition definition, DateOnly? from, DateOnly? to, MetricDimension? groupBy = null) => new(
        from,
        to,
        definition.Strokes,
        definition.Modes,
        definition.MinPercent,
        definition.MaxPercent,
        groupBy is { } dimension ? new[] { dimension } : Array.Empty<MetricDimension>(),
        definition.Measure);
}
