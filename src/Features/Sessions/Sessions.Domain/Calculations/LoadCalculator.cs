namespace Sessions.Domain.Calculations;

/// <param name="Load">Sum of distance x (effort / 100)^2, over 1000.</param>
/// <param name="UnratedDistance">Distance that had no intensity and so was counted at the default effort.</param>
public sealed record SessionLoad(double Load, int UnratedDistance);

/// <summary>The one place the session-load formula lives: <c>sum(distance x (effort / 100)^2) / 1000</c>, with effort the 1-10 intensity level times 10.</summary>
public static class LoadCalculator
{
    public static SessionLoad Compute(IEnumerable<(int Distance, int? Level)> parts, int defaultLevel)
    {
        double sum = 0;
        var unrated = 0;
        foreach (var (distance, level) in parts)
        {
            if (level is null)
            {
                unrated += distance;
            }

            var effort = (level ?? defaultLevel) * 10 / 100.0;
            sum += distance * effort * effort;
        }

        return new SessionLoad(sum / 1000, unrated);
    }
}
