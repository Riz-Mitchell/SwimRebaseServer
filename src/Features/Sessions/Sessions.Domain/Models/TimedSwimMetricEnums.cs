namespace Sessions.Domain.Models;

/// <summary>What timed-swim metrics can be grouped by. Mirrors <see cref="MetricDimension"/> for session volume, but over single timed efforts.</summary>
public enum TimedSwimDimension { Stroke, Mode, Distance, Course, StartType, Finish, Intensity, ResistanceKind, Day, Week, Month }

public enum TimedSwimMeasure
{
    /// <summary>How many swims.</summary>
    Count,

    /// <summary>The fastest time, in seconds.</summary>
    BestTime,

    /// <summary>The mean time, in seconds. Only comparable between swims of one distance and stroke.</summary>
    AvgTime,

    /// <summary>Mean intensity, in percent, of the swims that have one.</summary>
    AvgIntensity,
}
