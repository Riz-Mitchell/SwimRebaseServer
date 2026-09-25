using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

public enum MetricDimension { Stroke, Mode, Intensity, Day, Week, Month, Session }

public enum MetricMeasure
{
    /// <summary>Total distance.</summary>
    Distance,

    /// <summary>How many sessions.</summary>
    Sessions,

    /// <summary>Distance-weighted average intensity of the distance that has one.</summary>
    AvgIntensity,
}

/// <summary>
/// One question about what was swum: filter the facts, group them (0-2 dimensions), measure each group. Everything from "fly this month" to
/// "distance at intensity 8+ per week" is a query of this shape.
/// </summary>
public sealed record MetricsQuery(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<Stroke>? Strokes,
    IReadOnlyList<SwimMode>? Modes,
    int? MinIntensity,
    int? MaxIntensity,
    IReadOnlyList<MetricDimension> GroupBy,
    MetricMeasure Measure)
{
    public const int MaxDimensions = 2;

    /// <summary>True when an intensity bound is set, which excludes distance whose intensity is unspecified.</summary>
    public bool FiltersIntensity => MinIntensity is not null || MaxIntensity is not null;
}

/// <summary>The facts of one session narrowed to what a metric can group by, with distance summed. What the repository returns and the calculator reads.</summary>
public sealed record FactTotal(DateOnly PerformedOn, Guid SessionId, Stroke Stroke, SwimMode Mode, int? Intensity, DistanceUnit Unit, long Distance);

/// <param name="Keys">One value per group-by dimension, in order. Empty for an ungrouped query.</param>
/// <param name="Value">Null when the measure does not apply (an average over distance with no intensity).</param>
public sealed record MetricRow(IReadOnlyList<string> Keys, double? Value);

/// <param name="Units">Every distance unit present in the matching facts. More than one means distances mix metres and yards - nothing is converted.</param>
public sealed record MetricsResult(
    MetricMeasure Measure,
    IReadOnlyList<MetricDimension> GroupBy,
    IReadOnlyList<MetricRow> Rows,
    long TotalDistance,
    IReadOnlyList<DistanceUnit> Units);

/// <summary>The query cannot be answered as asked. The message is written for whoever (or whatever) made it.</summary>
public sealed class InvalidMetricsQueryException(string message) : Exception(message);
