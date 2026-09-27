using Sessions.Domain.Models;

namespace Sessions.Application.Services;

/// <summary>A split as it arrives over the wire.</summary>
public sealed record SplitDto(int DistanceFromStart, double Seconds, int? StrokeRateCyclesPerMinute)
{
    public Split ToSplit() => new() { DistanceFromStart = DistanceFromStart, Seconds = Seconds, StrokeRateCyclesPerMinute = StrokeRateCyclesPerMinute };
}

/// <summary>Resistance as it arrives over the wire.</summary>
public sealed record ResistanceDto(string Type, string? Load)
{
    public Resistance ToResistance() => new() { Type = Type, Load = Load };
}

/// <summary>
/// What the poolside quick-log form sends: one swim, not a tree. <see cref="SessionId"/> is optional - set when swum during a
/// practice, null for a standalone log (e.g. a race result). <see cref="IncludeInMetrics"/> defaults false: this feature is about
/// tracking times, not training volume.
/// </summary>
public sealed record LogTimedSwimRequest(
    Guid? SessionId,
    DateOnly PerformedOn,
    PoolCourse Course,
    int Dist,
    Stroke Stroke,
    SwimMode Mode,
    double Seconds,
    string? Effort,
    StartType? StartType,
    FinishReference? Finish,
    ResistanceDto? Resistance,
    List<SplitDto>? Splits,
    bool IncludeInMetrics = false);

/// <summary>What a timed-swim list can be narrowed by. Every field is optional; repeat a value in the wire form for several matches.</summary>
public sealed record TimedSwimFilter(
    Guid? SessionId,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<int>? Distances,
    IReadOnlyList<Stroke>? Strokes,
    int? MinIntensity,
    int? MaxIntensity,
    IReadOnlyList<StartType>? StartTypes,
    IReadOnlyList<string>? ResistanceTypes,
    bool? HasResistance);
