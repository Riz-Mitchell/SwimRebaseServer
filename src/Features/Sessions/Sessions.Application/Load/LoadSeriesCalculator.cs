using System.Globalization;
using Sessions.Application.Metrics;

namespace Sessions.Application.Load;

/// <summary>
/// Pure grouping of stored session loads into a series with a rolling baseline - kept out of SQL so it can be tested without a database.
/// A day, week or month is the mean load per day: its sessions' loads added up over the days it covers, rest days counting as 0.
/// </summary>
public static class LoadSeriesCalculator
{
    private const int Precision = 3;

    /// <param name="sessions">Must reach back as far as <see cref="FetchFrom"/> says, so the first rows have a baseline.</param>
    /// <param name="today">Days after it have not happened yet, so they never dilute the current period.</param>
    public static LoadResult Compute(LoadQuery query, IEnumerable<SessionLoadRow> sessions, DateOnly today)
    {
        Validate(query);

        var end = query.To is { } to && to < today ? to : today;
        var all = sessions.Where(s => s.PerformedOn <= end).OrderBy(s => s.PerformedOn).ThenBy(s => s.SessionId).ToList();

        var rows = query.GroupBy == LoadGrouping.Session ? BySession(query, all) : ByPeriod(query, all, end);
        var units = all.Where(s => query.From is null || s.PerformedOn >= query.From).Select(s => s.Unit).Distinct().Order().ToList();
        return new LoadResult(query.GroupBy, query.BaselinePeriods, rows, units);
    }

    /// <summary>The earliest date the repository needs sessions from, or null for no lower bound.</summary>
    public static DateOnly? FetchFrom(LoadQuery query) =>
        query.From is { } from && query.GroupBy != LoadGrouping.Session
            ? Back(query.GroupBy, PeriodStart(query.GroupBy, from), query.BaselinePeriods)
            : null;

    private static void Validate(LoadQuery query)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new InvalidLoadQueryException("from is after to.");
        }

        if (query.BaselinePeriods is < 1 or > LoadQuery.MaxBaselinePeriods)
        {
            throw new InvalidLoadQueryException($"baselinePeriods must be between 1 and {LoadQuery.MaxBaselinePeriods}.");
        }
    }

    private static List<LoadRow> BySession(LoadQuery query, List<SessionLoadRow> all)
    {
        var rows = new List<LoadRow>();
        for (var i = 0; i < all.Count; i++)
        {
            var session = all[i];
            if (query.From is { } from && session.PerformedOn < from)
            {
                continue;
            }

            var baseline = i >= query.BaselinePeriods
                ? Round(all.Skip(i - query.BaselinePeriods).Take(query.BaselinePeriods).Average(s => s.Load))
                : (double?)null;
            rows.Add(new LoadRow(Key(LoadGrouping.Day, session.PerformedOn), session.SessionId, Round(session.Load), Round(session.Load), 1, 1, session.UnratedDistance, baseline));
        }

        return rows;
    }

    private static List<LoadRow> ByPeriod(LoadQuery query, List<SessionLoadRow> all, DateOnly end)
    {
        var rows = new List<LoadRow>();
        DateOnly rangeStart;
        if (query.From is { } from)
        {
            rangeStart = from;
        }
        else if (all.Count > 0)
        {
            rangeStart = all[0].PerformedOn;
        }
        else
        {
            return rows;
        }

        if (rangeStart > end)
        {
            return rows;
        }

        var grouping = query.GroupBy;
        if (grouping == LoadGrouping.Day && end.DayNumber - rangeStart.DayNumber + 1 > LoadQuery.MaxDays)
        {
            throw new InvalidLoadQueryException($"a day-by-day series is limited to {LoadQuery.MaxDays} days - narrow from and to, or group by week or month.");
        }

        // With an explicit start, the periods before it are walked too (but not returned) so the first rows have a baseline.
        var firstVisible = PeriodStart(grouping, rangeStart);
        var first = query.From is null ? firstVisible : Back(grouping, firstVisible, query.BaselinePeriods);

        var values = new List<double>();
        for (var period = first; period <= end; period = Next(grouping, period))
        {
            var visible = period >= firstVisible;
            var windowStart = visible && rangeStart > period ? rangeStart : period;
            var periodEnd = Next(grouping, period).AddDays(-1);
            var windowEnd = periodEnd < end ? periodEnd : end;
            var days = windowEnd.DayNumber - windowStart.DayNumber + 1;

            var inPeriod = all.Where(s => s.PerformedOn >= windowStart && s.PerformedOn <= windowEnd).ToList();
            var total = inPeriod.Sum(s => s.Load);
            var value = total / days;

            var baseline = values.Count >= query.BaselinePeriods
                ? Round(values.Skip(values.Count - query.BaselinePeriods).Average())
                : (double?)null;
            values.Add(value);

            if (visible)
            {
                rows.Add(new LoadRow(
                    Key(grouping, period), null, Round(value), Round(total), inPeriod.Count, days, inPeriod.Sum(s => s.UnratedDistance), baseline));
            }
        }

        return rows;
    }

    private static DateOnly PeriodStart(LoadGrouping grouping, DateOnly date) => grouping switch
    {
        LoadGrouping.Week => MetricsCalculator.StartOfWeek(date),
        LoadGrouping.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };

    private static DateOnly Next(LoadGrouping grouping, DateOnly periodStart) => grouping switch
    {
        LoadGrouping.Week => periodStart.AddDays(7),
        LoadGrouping.Month => periodStart.AddMonths(1),
        _ => periodStart.AddDays(1),
    };

    private static DateOnly Back(LoadGrouping grouping, DateOnly periodStart, int periods) => grouping switch
    {
        LoadGrouping.Week => periodStart.AddDays(-7 * periods),
        LoadGrouping.Month => periodStart.AddMonths(-periods),
        _ => periodStart.AddDays(-periods),
    };

    private static string Key(LoadGrouping grouping, DateOnly periodStart) =>
        periodStart.ToString(grouping == LoadGrouping.Month ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static double Round(double value) => Math.Round(value, Precision);
}
