namespace Sessions.Domain.Models;

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
