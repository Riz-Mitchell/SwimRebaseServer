using System.Globalization;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>Pure grouping and measuring over <see cref="TimedSwimFact"/>s - kept out of SQL so it can be tested without a database.</summary>
public static class TimedSwimMetricsCalculator
{
    private const string None = "none";
    private const string Unspecified = "unspecified";

    public static TimedSwimMetricsResult Compute(TimedSwimMetricsQuery query, IEnumerable<TimedSwimFact> facts)
    {
        Validate(query);

        var matching = facts.Where(f => Matches(query, f)).ToList();
        var groups = new Dictionary<string, (string[] Keys, List<TimedSwimFact> Facts)>();
        foreach (var fact in matching)
        {
            foreach (var keys in KeyCombinations(query, fact))
            {
                var id = string.Join('\u001f', keys);
                if (!groups.TryGetValue(id, out var group))
                {
                    groups[id] = group = (keys, []);
                }

                group.Facts.Add(fact);
            }
        }

        var timeMeasure = query.Measure is TimedSwimMeasure.BestTime or TimedSwimMeasure.AvgTime;
        var mixed = timeMeasure && groups.Values.Any(g => g.Facts.Select(f => (f.Dist, f.Stroke)).Distinct().Count() > 1);

        var rows = groups.Values.Select(g => new MetricRow(g.Keys, Measure(query.Measure, g.Facts))).ToList();
        var ordered = InKeyOrder(query)
            ? rows.OrderBy(r => r.Keys, new KeyOrder(query.GroupBy, query.EffectiveScale)).ToList()
            : (timeMeasure
                ? rows.OrderBy(r => r.Value is null).ThenBy(r => r.Value)
                : rows.OrderByDescending(r => r.Value)).ThenBy(r => r.Keys, new KeyOrder(query.GroupBy, query.EffectiveScale)).ToList();

        return new TimedSwimMetricsResult(query.Measure, query.GroupBy, ordered, matching.Count, matching.Select(f => f.Unit).Distinct().Order().ToList(), query.EffectiveScale, mixed);
    }

    private static void Validate(TimedSwimMetricsQuery query)
    {
        if (query.GroupBy.Count > TimedSwimMetricsQuery.MaxDimensions)
        {
            throw new InvalidMetricsQueryException($"group by at most {TimedSwimMetricsQuery.MaxDimensions} dimensions.");
        }

        if (query.GroupBy.Distinct().Count() != query.GroupBy.Count)
        {
            throw new InvalidMetricsQueryException("group-by dimensions must be different.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new InvalidMetricsQueryException("from is after to.");
        }

        if (query.MinPercent is { } min && query.MaxPercent is { } max && min > max)
        {
            throw new InvalidMetricsQueryException("the lowest intensity is above the highest.");
        }
    }

    private static bool Matches(TimedSwimMetricsQuery q, TimedSwimFact f) =>
        (q.From is null || f.PerformedOn >= q.From)
        && (q.To is null || f.PerformedOn <= q.To)
        && (q.Distances is not { Count: > 0 } || q.Distances.Contains(f.Dist))
        && (q.Strokes is not { Count: > 0 } || q.Strokes.Contains(f.Stroke))
        && (q.Modes is not { Count: > 0 } || q.Modes.Contains(f.Mode))
        && (q.Courses is not { Count: > 0 } || q.Courses.Contains(f.Course))
        && (q.StartTypes is not { Count: > 0 } || (f.StartType is { } start && q.StartTypes.Contains(start)))
        && (q.Finishes is not { Count: > 0 } || (f.Finish is { } finish && q.Finishes.Contains(finish)))
        && (q.ResistanceKinds is not { Count: > 0 } || f.ResistanceKinds.Any(q.ResistanceKinds.Contains))
        && (q.HasResistance is null || (f.ResistanceKinds.Count > 0) == q.HasResistance)
        && (q.MinPercent is null || f.Intensity >= q.MinPercent)
        && (q.MaxPercent is null || f.Intensity <= q.MaxPercent);

    private static double? Measure(TimedSwimMeasure measure, List<TimedSwimFact> group) => measure switch
    {
        TimedSwimMeasure.Count => group.Count,
        TimedSwimMeasure.BestTime => group.Min(f => f.Seconds),
        TimedSwimMeasure.AvgTime => Math.Round(group.Average(f => f.Seconds), 2),
        _ => group.Where(f => f.Intensity is not null).Select(f => (double)f.Intensity!.Value).DefaultIfEmpty(double.NaN).Average() is var avg && !double.IsNaN(avg) ? Math.Round(avg, 2) : null,
    };

    /// <summary>A swim has one value per dimension, except resistance kind: a swim with sponges and a parachute counts under both kinds.</summary>
    private static IEnumerable<string[]> KeyCombinations(TimedSwimMetricsQuery query, TimedSwimFact fact)
    {
        IEnumerable<string[]> combos = [[]];
        foreach (var dimension in query.GroupBy)
        {
            var values = Keys(dimension, fact, query.EffectiveScale);
            combos = combos.SelectMany(prefix => values.Select(v => (string[])[.. prefix, v]));
        }

        return combos;
    }

    private static IReadOnlyList<string> Keys(TimedSwimDimension dimension, TimedSwimFact f, EffortScale scale) => dimension switch
    {
        TimedSwimDimension.Stroke => [f.Stroke.ToString().ToLowerInvariant()],
        TimedSwimDimension.Mode => [f.Mode.ToString().ToLowerInvariant()],
        TimedSwimDimension.Distance => [f.Dist.ToString("0.##", CultureInfo.InvariantCulture)],
        TimedSwimDimension.Course => [f.Course.ToString().ToLowerInvariant()],
        TimedSwimDimension.StartType => [f.StartType?.ToString().ToLowerInvariant() ?? Unspecified],
        TimedSwimDimension.Finish => [f.Finish?.ToString().ToLowerInvariant() ?? Unspecified],
        TimedSwimDimension.Intensity => [scale.Bucket(f.Intensity)],
        TimedSwimDimension.ResistanceKind => f.ResistanceKinds.Count == 0 ? [None] : [.. f.ResistanceKinds.Distinct().Select(k => k.ToString().ToLowerInvariant())],
        TimedSwimDimension.Day => [f.PerformedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
        TimedSwimDimension.Week => [StartOfWeek(f.PerformedOn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
        _ => [f.PerformedOn.ToString("yyyy-MM", CultureInfo.InvariantCulture)],
    };

    /// <summary>Weeks start on Monday; a week is named by its Monday.</summary>
    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Time, distance and intensity read in order; everything else reads best or biggest first.</summary>
    private static bool InKeyOrder(TimedSwimMetricsQuery q) =>
        q.GroupBy.Any(d => d is TimedSwimDimension.Day or TimedSwimDimension.Week or TimedSwimDimension.Month or TimedSwimDimension.Intensity or TimedSwimDimension.Distance);

    private sealed class KeyOrder(IReadOnlyList<TimedSwimDimension> dimensions, EffortScale scale) : IComparer<IReadOnlyList<string>>
    {
        public int Compare(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            for (var i = 0; i < Math.Min(x!.Count, y!.Count); i++)
            {
                var result = dimensions[i] switch
                {
                    TimedSwimDimension.Intensity => scale.Order(x[i]).CompareTo(scale.Order(y[i])),
                    TimedSwimDimension.Distance => decimal.Parse(x[i], CultureInfo.InvariantCulture).CompareTo(decimal.Parse(y[i], CultureInfo.InvariantCulture)),
                    _ => string.CompareOrdinal(x[i], y[i]),
                };
                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        }
    }
}
