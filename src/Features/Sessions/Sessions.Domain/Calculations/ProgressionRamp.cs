using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>How many reps of an item were swum at one intensity.</summary>
public readonly record struct RampSlice(int Percent, int Reps);

/// <summary>
/// Spreads a descend over the reps of an item. Between two zones the reps step through the zones in between, evenly spaced with a tie
/// going to the easier zone (A1 to A2 over 3 reps is A1, A1, A2); any other endpoints are interpolated in percent.
/// </summary>
public static class ProgressionRamp
{
    /// <summary>Null when the item has no descend, or its endpoints are not resolved yet. Slices are ordered easiest first, one per distinct intensity.</summary>
    public static IReadOnlyList<RampSlice>? Slices(SetItem item)
    {
        if (item.Progression is not { FromIntensity: { } from, ToIntensity: { } to } progression || item.Reps < 2)
        {
            return null;
        }

        var span = Math.Clamp(progression.Span ?? item.Reps, 2, item.Reps);
        var stops = ZoneStops(from, to);
        var reps = new SortedDictionary<int, int>();
        for (var start = 0; start < item.Reps; start += span)
        {
            var length = Math.Min(span, item.Reps - start);
            for (var rep = 0; rep < length; rep++)
            {
                var percent = PercentAt(rep, length, from.Percent, to.Percent, stops);
                reps[percent] = reps.GetValueOrDefault(percent) + 1;
            }
        }

        return [.. reps.Select(r => new RampSlice(r.Key, r.Value))];
    }

    private static int PercentAt(int rep, int length, int from, int to, IReadOnlyList<int>? stops)
    {
        if (length == 1)
        {
            return from;
        }

        var last = length - 1;
        if (stops is null)
        {
            return from + (int)Math.Round((to - from) * (double)rep / last, MidpointRounding.AwayFromZero);
        }

        // round(rep * (stops - 1) / last) with halves going down, in integers.
        return stops[(2 * rep * (stops.Count - 1) + last - 1) / (2 * last)];
    }

    private static List<int>? ZoneStops(Intensity from, Intensity to)
    {
        if (from.Scale != EffortVocabulary.ZonesScale || to.Scale != EffortVocabulary.ZonesScale)
        {
            return null;
        }

        var stops = EffortVocabulary.Default.Entries
            .Where(e => e.Scale == EffortVocabulary.ZonesScale && e.Percent >= from.Percent && e.Percent <= to.Percent)
            .Select(e => e.Percent)
            .Distinct()
            .Order()
            .ToList();
        return stops.Count < 2 ? null : stops;
    }
}
