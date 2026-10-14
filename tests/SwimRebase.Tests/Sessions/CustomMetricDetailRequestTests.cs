using Sessions.Application.Metrics;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class CustomMetricDetailRequestTests
{
    private static readonly DateOnly ServerToday = new(2026, 10, 14);

    private static CustomMetricDetailRequest Ask(string today, string? window = null, string? groupBy = null) =>
        CustomMetricDetailRequest.Parse(today, window, groupBy, ServerToday);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("14-10-2026")]
    [InlineData("tomorrow")]
    [InlineData("1999-12-31")]
    [InlineData("2026-10-16")]
    public void A_missing_unreadable_or_out_of_range_today_is_refused(string? today)
    {
        Assert.Throws<InvalidCustomMetricException>(() => CustomMetricDetailRequest.Parse(today, null, null, ServerToday));
    }

    [Fact]
    public void A_swimmer_a_day_ahead_of_the_server_is_not_in_the_future()
    {
        Assert.Equal(new DateOnly(2026, 10, 15), Ask("2026-10-15").Today);
    }

    [Theory]
    [InlineData("someday", null)]
    [InlineData("1", null)]
    [InlineData(null, "stroke")]
    [InlineData(null, "bogus")]
    [InlineData(null, "2")]
    public void An_unknown_window_or_grouping_is_refused(string? window, string? groupBy)
    {
        Assert.Throws<InvalidCustomMetricException>(() => Ask("2026-10-14", window, groupBy));
    }

    [Fact]
    public void Windows_and_groupings_are_read_in_any_case()
    {
        var request = Ask("2026-10-14", "ThisMonth", "WEEK");

        Assert.Equal(MetricWindow.ThisMonth, request.Window);
        Assert.Equal(MetricDimension.Week, request.GroupBy);
    }
}
