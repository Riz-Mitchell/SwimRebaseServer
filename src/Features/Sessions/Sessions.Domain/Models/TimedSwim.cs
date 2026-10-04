namespace Sessions.Domain.Models;

public enum StartType { Push, Dive }

public enum FinishReference { Hand, Head, Feet }

/// <summary>A checkpoint before the finish: how far in, how long it took to get there, and the stroke rate at that point.</summary>
public sealed record Split
{
    public decimal DistanceFromStart { get; init; }

    public double Seconds { get; init; }

    /// <summary>Cycles/min, not individual arm strokes: one cycle is a full stroke cycle (e.g. two pulls for free/back).</summary>
    public int? StrokeRateCyclesPerMinute { get; init; }
}

/// <summary>What resistance, if any, the swim was done with.</summary>
public sealed record Resistance
{
    public string Type { get; init; } = string.Empty;

    public string? Load { get; init; }
}

/// <summary>
/// How one swim went: a time for a distance+stroke, logged separately from training-volume sessions. Usually swum during a
/// session (<see cref="SessionId"/> set); sometimes standalone, e.g. a race result. The "event" is just <see cref="Dist"/> +
/// <see cref="Stroke"/> (e.g. "50 Free") - no separate meet/event field.
/// </summary>
public class TimedSwim
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>Null when logged standalone (e.g. a race result with no training session behind it).</summary>
    public Guid? SessionId { get; set; }

    public DateOnly PerformedOn { get; set; }

    public PoolCourse Course { get; set; }

    public decimal Dist { get; set; }

    public Stroke Stroke { get; set; }

    public SwimMode Mode { get; set; } = SwimMode.Swim;

    /// <summary>The swim's total time. Always required - splits are optional detail on top of it, not a substitute for it.</summary>
    public double Seconds { get; set; }

    /// <summary>Effort as written or said ("easy", "80%", "race pace"). Mapped to <see cref="Intensity"/> on save.</summary>
    public string? Effort { get; set; }

    /// <summary>On the global 1-10 scale; null means unspecified. Only the level is kept, matching <see cref="SessionFact.Intensity"/>.</summary>
    public int? Intensity { get; set; }

    public StartType? StartType { get; set; }

    public FinishReference? Finish { get; set; }

    public string? ResistanceType { get; set; }

    public string? ResistanceLoad { get; set; }

    /// <summary>A serialized <see cref="Split"/> list ("[]" when there are none). Kept as JSON rather than a child table: read as a
    /// whole with the swim, never queried at the SQL level.</summary>
    public string SplitsJson { get; set; } = "[]";

    /// <summary>Denormalized: <see cref="Calculations.StrokeRateCalculator.Compute"/> over the splits, stored at write time.</summary>
    public double? StrokeRateCyclesPerMinute { get; set; }

    /// <summary>Whether this swim counts toward the volume-metrics pipeline (GET /api/sessions/metrics). Only meaningful when
    /// <see cref="SessionId"/> is set - a standalone swim has no session to be training volume of.</summary>
    public bool IncludeInMetrics { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DistanceUnit Unit => Course == PoolCourse.Scy ? DistanceUnit.Yards : DistanceUnit.Metres;
}
