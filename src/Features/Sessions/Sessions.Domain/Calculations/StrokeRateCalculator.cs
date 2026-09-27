using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Turns a swim's split-level stroke-rate readings into one figure for the whole swim.</summary>
public static class StrokeRateCalculator
{
    /// <summary>Null if no split recorded a rate, the reading itself if exactly one did, otherwise the simple average.</summary>
    public static double? Compute(IReadOnlyList<Split> splits)
    {
        var rates = splits.Where(s => s.StrokeRateCyclesPerMinute is not null).Select(s => (double)s.StrokeRateCyclesPerMinute!.Value).ToList();
        return rates.Count == 0 ? null : Math.Round(rates.Average(), 1);
    }
}
