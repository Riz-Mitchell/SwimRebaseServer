using System.Globalization;
using Sessions.Domain.Calculations;

namespace Sessions.Application.Metrics;

/// <summary>Pure grouping and measuring over <see cref="FactTotal"/>s - kept out of SQL so it can be tested without a database.</summary>
public static class MetricsCalculator
{
    public static MetricsResult Compute(MetricsQuery query, IEnumerable<FactTotal> totals)
    {
        Validate(query);

        // The repository filters in SQL; the same rule is applied here so the calculator is correct on its own.
        var facts = totals.Where(f => Matches(query, f)).ToList();

        var grouped = facts
            .GroupBy(f => query.GroupBy.Select(d => Key(d, f)).ToArray(), KeyComparer.Instance)
            .Select(g => new MetricRow(g.Key, Measure(query.Measure, g)));

        // Time and intensity read in order; everything else (stroke, mode, session) reads biggest first.
        var inOrder = query.GroupBy.Any(d => d is MetricDimension.Day or MetricDimension.Week or MetricDimension.Month or MetricDimension.Intensity);
        var rows = (inOrder
            ? grouped.OrderBy(r => r.Keys, KeyOrder.Instance).ThenByDescending(r => r.Value)
            : grouped.OrderByDescending(r => r.Value).ThenBy(r => r.Keys, KeyOrder.Instance)).ToList();

        return new MetricsResult(query.Measure, query.GroupBy, rows, facts.Sum(f => f.Distance), facts.Select(f => f.Unit).Distinct().Order().ToList());
    }

    private static void Validate(MetricsQuery query)
    {
        if (query.GroupBy.Count > MetricsQuery.MaxDimensions)
        {
            throw new InvalidMetricsQueryException($"group by at most {MetricsQuery.MaxDimensions} dimensions.");
        }

        if (query.GroupBy.Distinct().Count() != query.GroupBy.Count)
        {
            throw new InvalidMetricsQueryException("group-by dimensions must be different.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new InvalidMetricsQueryException("from is after to.");
        }

        if (query.MinIntensity is { } min && query.MaxIntensity is { } max && min > max)
        {
            throw new InvalidMetricsQueryException("minIntensity is above maxIntensity.");
        }
    }

    private static bool Matches(MetricsQuery q, FactTotal f) =>
        (q.From is null || f.PerformedOn >= q.From)
        && (q.To is null || f.PerformedOn <= q.To)
        && (q.Strokes is not { Count: > 0 } || q.Strokes.Contains(f.Stroke))
        && (q.Modes is not { Count: > 0 } || q.Modes.Contains(f.Mode))
        && (q.MinIntensity is null || f.Intensity >= q.MinIntensity)
        && (q.MaxIntensity is null || f.Intensity <= q.MaxIntensity);

    private static double? Measure(MetricMeasure measure, IEnumerable<FactTotal> group) => measure switch
    {
        MetricMeasure.Distance => group.Sum(f => f.Distance),
        MetricMeasure.Sessions => group.Select(f => f.SessionId).Distinct().Count(),
        _ => WeightedIntensity(group),
    };

    private static double? WeightedIntensity(IEnumerable<FactTotal> group)
    {
        var withIntensity = group.Where(f => f.Intensity is not null).ToList();
        var distance = withIntensity.Sum(f => f.Distance);
        return distance == 0 ? null : Math.Round(withIntensity.Sum(f => f.Distance * (double)f.Intensity!.Value) / distance, 2);
    }

    private static string Key(MetricDimension dimension, FactTotal f) => dimension switch
    {
        MetricDimension.Stroke => f.Stroke.ToString().ToLowerInvariant(),
        MetricDimension.Mode => f.Mode.ToString().ToLowerInvariant(),
        MetricDimension.Intensity => f.Intensity?.ToString(CultureInfo.InvariantCulture) ?? SessionBreakdown.Unspecified,
        MetricDimension.Day => f.PerformedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        MetricDimension.Week => StartOfWeek(f.PerformedOn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        MetricDimension.Month => f.PerformedOn.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        MetricDimension.Session => f.SessionId.ToString(),
        _ => string.Empty,
    };

    /// <summary>Weeks start on Monday; a week is named by its Monday.</summary>
    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private sealed class KeyComparer : IEqualityComparer<string[]>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals(string[]? x, string[]? y) => x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(string[] obj) => obj.Aggregate(17, (hash, key) => HashCode.Combine(hash, key));
    }

    /// <summary>Keys sort ordinally, and numeric levels numerically, so dates and weeks read in time order and intensity 2 comes before 10.</summary>
    private sealed class KeyOrder : IComparer<IReadOnlyList<string>>
    {
        public static readonly KeyOrder Instance = new();

        public int Compare(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
        {
            for (var i = 0; i < Math.Min(x!.Count, y!.Count); i++)
            {
                var result = int.TryParse(x[i], out var a) && int.TryParse(y[i], out var b) ? a.CompareTo(b) : string.CompareOrdinal(x[i], y[i]);
                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        }
    }
}
