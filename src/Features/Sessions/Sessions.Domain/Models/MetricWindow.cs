namespace Sessions.Domain.Models;

public enum MetricWindow { Last7Days, Last30Days, ThisWeek, ThisMonth, AllTime }

public static class MetricWindows
{
    /// <summary>The dates a window covers as of <paramref name="today"/>, which the caller supplies so it is the swimmer's day, not the server's. Weeks start on Monday.</summary>
    public static (DateOnly? From, DateOnly? To) Resolve(MetricWindow window, DateOnly today) => window switch
    {
        MetricWindow.Last7Days => (today.AddDays(-6), today),
        MetricWindow.Last30Days => (today.AddDays(-29), today),
        MetricWindow.ThisWeek => (StartOfWeek(today), today),
        MetricWindow.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
        _ => (null, null),
    };

    /// <summary>
    /// The whole period <paramref name="window"/> covers around <paramref name="anchor"/>: a calendar week runs Monday to Sunday and a month to its
    /// last day, so stepping the anchor back one period lands on the previous whole period. All time has no dates of its own.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) Period(MetricWindow window, DateOnly anchor) => window switch
    {
        MetricWindow.Last7Days => (anchor.AddDays(-6), anchor),
        MetricWindow.Last30Days => (anchor.AddDays(-29), anchor),
        MetricWindow.ThisWeek => Week(anchor),
        MetricWindow.ThisMonth => (new DateOnly(anchor.Year, anchor.Month, 1), new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))),
        _ => (null, null),
    };

    /// <summary>The anchor that makes <see cref="Period"/> land on the period just before the one around <paramref name="anchor"/>; null for all time, which has none.</summary>
    public static DateOnly? PreviousAnchor(MetricWindow window, DateOnly anchor) => window switch
    {
        MetricWindow.Last7Days or MetricWindow.ThisWeek => anchor.AddDays(-7),
        MetricWindow.Last30Days => anchor.AddDays(-30),
        MetricWindow.ThisMonth => anchor.AddMonths(-1),
        _ => null,
    };

    /// <summary>
    /// The previous whole period of <paramref name="window"/> as of <paramref name="today"/>, cut at the same point in it that <paramref name="today"/> is in the current one
    /// (a month is clamped to its length). Null for the rolling windows, which are always compared whole, and for all time.
    /// </summary>
    public static (DateOnly From, DateOnly To)? PreviousToDate(MetricWindow window, DateOnly today)
    {
        if (window is not (MetricWindow.ThisWeek or MetricWindow.ThisMonth))
        {
            return null;
        }

        var elapsed = today.DayNumber - Resolve(window, today).From!.Value.DayNumber;
        var (from, to) = Period(window, PreviousAnchor(window, today)!.Value);
        var cut = from!.Value.AddDays(elapsed);
        return (from.Value, cut < to ? cut : to!.Value);
    }

    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static (DateOnly? From, DateOnly? To) Week(DateOnly anchor)
    {
        var monday = StartOfWeek(anchor);
        return (monday, monday.AddDays(6));
    }
}
