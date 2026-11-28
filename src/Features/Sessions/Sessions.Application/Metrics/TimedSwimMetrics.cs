using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>
/// One question about timed swims: filter them, group them (0-2 dimensions), measure each group. "Best 25 free by start type" and
/// "average 50 free time by resistance kind" are queries of this shape. Deliberately separate from <see cref="MetricsQuery"/>:
/// timed swims are performance records, not training volume.
/// </summary>
public sealed record TimedSwimMetricsQuery(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<decimal>? Distances,
    IReadOnlyList<Stroke>? Strokes,
    IReadOnlyList<SwimMode>? Modes,
    IReadOnlyList<PoolCourse>? Courses,
    IReadOnlyList<StartType>? StartTypes,
    IReadOnlyList<FinishReference>? Finishes,
    IReadOnlyList<ResistanceKind>? ResistanceKinds,
    bool? HasResistance,
    int? MinPercent,
    int? MaxPercent,
    IReadOnlyList<TimedSwimDimension> GroupBy,
    TimedSwimMeasure Measure,
    string? ScaleId = null,
    EffortScale? Scale = null)
{
    public const int MaxDimensions = 2;

    /// <summary>How the intensity dimension is bucketed once the service has resolved <see cref="ScaleId"/>; the tens scale until then.</summary>
    public EffortScale EffectiveScale => Scale ?? EffortScale.Tens;
}

/// <summary>A timed swim narrowed to what a metric can group by. What the repository returns and the calculator reads.</summary>
public sealed record TimedSwimFact(
    DateOnly PerformedOn,
    PoolCourse Course,
    decimal Dist,
    Stroke Stroke,
    SwimMode Mode,
    double Seconds,
    int? Intensity,
    StartType StartType,
    FinishReference Finish,
    IReadOnlyList<ResistanceKind> ResistanceKinds)
{
    public DistanceUnit Unit => Course == PoolCourse.Scy ? DistanceUnit.Yards : DistanceUnit.Metres;
}

/// <param name="TotalSwims">How many swims matched the filters.</param>
/// <param name="Units">Every distance unit present in the matching swims. More than one means yards and metres are mixed - nothing is converted.</param>
/// <param name="MixedEvents">True when a time measure grouped swims of different distance or stroke, so that time is not comparable. Group by distance and stroke to avoid it.</param>
public sealed record TimedSwimMetricsResult(
    TimedSwimMeasure Measure,
    IReadOnlyList<TimedSwimDimension> GroupBy,
    IReadOnlyList<MetricRow> Rows,
    int TotalSwims,
    IReadOnlyList<DistanceUnit> Units,
    EffortScale Scale,
    bool MixedEvents);
