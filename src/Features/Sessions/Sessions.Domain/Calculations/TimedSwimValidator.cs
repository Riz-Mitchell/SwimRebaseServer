using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Problems a re-ask can fix: impossible numbers, splits out of order, resistance missing its type.</summary>
public static class TimedSwimValidator
{
    public static List<string> Validate(int dist, double seconds, Resistance? resistance, IReadOnlyList<Split> splits)
    {
        var errors = new List<string>();
        if (dist < 1) errors.Add("dist must be >= 1");
        if (seconds <= 0) errors.Add("seconds must be > 0");
        if (resistance is { Type.Length: 0 }) errors.Add("resistance type is required when resistance is set");

        var lastDistance = 0;
        var lastSeconds = 0.0;
        for (var i = 0; i < splits.Count; i++)
        {
            var split = splits[i];
            var path = $"splits[{i}]";

            if (split.DistanceFromStart <= lastDistance)
            {
                errors.Add($"{path}: distanceFromStart must increase from the previous split");
            }

            if (split.DistanceFromStart >= dist)
            {
                errors.Add($"{path}: distanceFromStart must be less than dist ({dist}) - the finish time is the swim's own seconds, not a split");
            }

            if (split.Seconds <= lastSeconds)
            {
                errors.Add($"{path}: seconds must increase from the previous split");
            }

            if (split.Seconds >= seconds)
            {
                errors.Add($"{path}: seconds must be less than the swim's total seconds");
            }

            if (split.StrokeRateCyclesPerMinute is { } rate && rate <= 0)
            {
                errors.Add($"{path}: strokeRateCyclesPerMinute must be > 0");
            }

            lastDistance = split.DistanceFromStart;
            lastSeconds = split.Seconds;
        }

        return errors;
    }
}
