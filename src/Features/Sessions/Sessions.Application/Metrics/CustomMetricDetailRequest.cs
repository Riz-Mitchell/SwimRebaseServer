using System.Globalization;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>What a caller may change about one look at a saved metric: the anchor date, and optionally the window and series grouping for this look only. <c>ThenBy</c> splits each bar by effort band, bucketed by <c>ScaleId</c> (else the swimmer's saved scale). <c>IncludePrevious</c> adds the period before.</summary>
public sealed record CustomMetricDetailRequest(DateOnly Today, MetricWindow? Window, MetricDimension? GroupBy, MetricDimension? ThenBy = null, string? ScaleId = null, bool IncludePrevious = false)
{
    /// <summary>The most buckets one series may have.</summary>
    public const int MaxBuckets = 366;

    private static readonly DateOnly Earliest = new(2000, 1, 1);

    /// <exception cref="InvalidCustomMetricException">Any value is missing where required, unreadable, or out of range.</exception>
    public static CustomMetricDetailRequest Parse(
        string? today, string? window, string? groupBy, DateOnly serverToday, string? thenBy = null, string? scale = null, string? include = null) =>
        new(ParseToday(today, serverToday), ParseOptional<MetricWindow>(window, "window"), ParseGroupBy(groupBy), ParseThenBy(thenBy), scale, ParseInclude(include));

    /// <summary>
    /// The swimmer's own date. A day past the server's UTC date is allowed because a swimmer east of UTC is legitimately there;
    /// anything beyond that is a wrong clock.
    /// </summary>
    public static DateOnly ParseToday(string? text, DateOnly serverToday)
    {
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var today))
        {
            throw new InvalidCustomMetricException("today is required, as yyyy-MM-dd");
        }

        if (today < Earliest)
        {
            throw new InvalidCustomMetricException("today is too far in the past");
        }

        return today > serverToday.AddDays(1) ? throw new InvalidCustomMetricException("today is in the future") : today;
    }

    /// <summary>A series that would have more than <see cref="MaxBuckets"/> bars is refused, from the dates alone, before any query runs.</summary>
    public static void EnsureChartable(MetricDimension? groupBy, DateOnly from, DateOnly to)
    {
        var (count, unit) = groupBy switch
        {
            MetricDimension.Day => (to.DayNumber - from.DayNumber + 1, "days"),
            MetricDimension.Week => ((MetricWindows.StartOfWeek(to).DayNumber - MetricWindows.StartOfWeek(from).DayNumber) / 7 + 1, "weeks"),
            MetricDimension.Month => ((to.Year - from.Year) * 12 + to.Month - from.Month + 1, "months"),
            _ => (0, string.Empty),
        };

        if (count > MaxBuckets)
        {
            throw new InvalidCustomMetricException($"that would chart {count} {unit}; the most is {MaxBuckets}. Group by a longer period or choose a shorter window");
        }
    }

    private static MetricDimension? ParseGroupBy(string? text)
    {
        var dimension = ParseOptional<MetricDimension>(text, "groupBy");
        return dimension is null or MetricDimension.Day or MetricDimension.Week or MetricDimension.Month
            ? dimension
            : throw new InvalidCustomMetricException("groupBy must be day, week or month");
    }

    /// <exception cref="InvalidCustomMetricException">The value is not <c>previous</c>.</exception>
    public static bool ParseInclude(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Trim().Equals("previous", StringComparison.OrdinalIgnoreCase)
            ? true
            : throw new InvalidCustomMetricException($"unknown include '{text}'");
    }

    private static MetricDimension? ParseThenBy(string? text)
    {
        var dimension = ParseOptional<MetricDimension>(text, "thenBy");
        return dimension is null or MetricDimension.Intensity ? dimension : throw new InvalidCustomMetricException("thenBy must be intensity");
    }

    private static T? ParseOptional<T>(string? text, string name) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Numbers parse as enum values, so they are refused: the API speaks names.
        return !int.TryParse(text, out _) && Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidCustomMetricException($"unknown {name} '{text}'");
    }
}
