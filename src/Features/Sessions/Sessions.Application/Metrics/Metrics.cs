using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

public enum MetricDimension { Stroke, Mode, Intensity, Day, Week, Month, Session }

public enum MetricMeasure
{
    /// <summary>Total distance.</summary>
    Distance,

    /// <summary>How many sessions.</summary>
    Sessions,

    /// <summary>Distance-weighted average intensity, in percent, of the distance that has one.</summary>
    AvgIntensity,
}

/// <summary>
/// One question about what was swum: filter the facts, group them (0-2 dimensions), measure each group. Everything from "fly this month" to
/// "distance at 80%+ effort per week" is a query of this shape.
/// </summary>
public sealed record MetricsQuery(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<Stroke>? Strokes,
    IReadOnlyList<SwimMode>? Modes,
    int? MinPercent,
    int? MaxPercent,
    IReadOnlyList<MetricDimension> GroupBy,
    MetricMeasure Measure,
    string? ScaleId = null,
    EffortScale? Scale = null,
    Guid? SessionId = null,
    bool ApplyDefaultEffort = true,
    int? DefaultEffort = null)
{
    public const int MaxDimensions = 2;

    /// <summary>True when an intensity bound is set, which excludes distance whose intensity is unspecified.</summary>
    public bool FiltersIntensity => MinPercent is not null || MaxPercent is not null;

    /// <summary>How the intensity dimension is bucketed once <see cref="MetricsService"/> has resolved <see cref="ScaleId"/>; the tens scale until then.</summary>
    public EffortScale EffectiveScale => Scale ?? EffortScale.Tens;
}

/// <summary>The facts of one session narrowed to what a metric can group by, with distance summed. What the repository returns and the calculator reads.</summary>
public sealed record FactTotal(DateOnly PerformedOn, Guid SessionId, Stroke Stroke, SwimMode Mode, int? Intensity, DistanceUnit Unit, decimal Distance);

/// <param name="Keys">One value per group-by dimension, in order. Empty for an ungrouped query.</param>
/// <param name="Value">Null when the measure does not apply (an average over distance with no intensity).</param>
public sealed record MetricRow(IReadOnlyList<string> Keys, double? Value);

/// <param name="Scale">The effort scale intensity was bucketed by.</param>
/// <param name="DefaultEffort">The percent that distance with no stated effort was counted at; null when that was not applied.</param>
/// <param name="AssumedDistance">How much distance was counted at <paramref name="DefaultEffort"/>.</param>
/// <param name="Units">Every distance unit present in the matching facts. More than one means distances mix metres and yards - nothing is converted.</param>
public sealed record MetricsResult(
    MetricMeasure Measure,
    IReadOnlyList<MetricDimension> GroupBy,
    IReadOnlyList<MetricRow> Rows,
    decimal TotalDistance,
    IReadOnlyList<DistanceUnit> Units,
    EffortScale Scale,
    int? DefaultEffort,
    decimal AssumedDistance);

/// <summary>The query cannot be answered as asked. The message is written for whoever (or whatever) made it.</summary>
public sealed class InvalidMetricsQueryException(string message) : Exception(message);
