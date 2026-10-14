namespace Sessions.Domain.Models;

public enum MetricWindow { Last7Days, Last30Days, ThisWeek, ThisMonth, AllTime }

public static class MetricWindows
{
    /// <summary>The dates a window covers as of <paramref name="today"/>, which the caller supplies so it is the swimmer's day, not the server's. Weeks start on Monday.</summary>
    public static (DateOnly? From, DateOnly? To) Resolve(MetricWindow window, DateOnly today) => window switch
    {
        MetricWindow.Last7Days => (today.AddDays(-6), today),
        MetricWindow.Last30Days => (today.AddDays(-29), today),
        MetricWindow.ThisWeek => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
        MetricWindow.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
        _ => (null, null),
    };
}
