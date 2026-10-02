using Sessions.Application.Load;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class LoadSeriesCalculatorTests
{
    // Mon 2026-09-14 (A), Wed 2026-09-16 (B), Tue 2026-09-22 (C, the following week), Fri 2026-10-02 (D, next month).
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    private static readonly DateOnly Sunday = new(2026, 10, 4);

    private static List<SessionLoadRow> Sessions() =>
    [
        Row(A, "2026-09-14", 1.0),
        Row(B, "2026-09-16", 2.0),
        Row(C, "2026-09-22", 3.0),
        Row(D, "2026-10-02", 4.0),
    ];

    private static SessionLoadRow Row(Guid id, string date, double load, int unrated = 0) =>
        new(id, DateOnly.Parse(date), load, unrated, DistanceUnit.Metres);

    private static LoadQuery Query(LoadGrouping groupBy, int baseline = 4, string? from = null, string? to = null) =>
        new(from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), groupBy, baseline);

    [Fact]
    public void Grouped_by_session_each_row_is_that_sessions_own_load()
    {
        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Session, baseline: 2), Sessions(), Sunday);

        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, result.Rows.Select(r => r.Load));
        Assert.Equal(new Guid?[] { A, B, C, D }, result.Rows.Select(r => r.SessionId));
        Assert.All(result.Rows, r => Assert.Equal(1, r.SessionCount));
    }

    [Fact]
    public void The_baseline_is_the_mean_of_the_rows_before_and_empty_until_there_are_enough()
    {
        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Session, baseline: 2), Sessions(), Sunday);

        Assert.Equal(new double?[] { null, null, 1.5, 2.5 }, result.Rows.Select(r => r.Baseline));
    }

    [Fact]
    public void A_week_is_the_mean_load_per_day_with_rest_days_counting_as_zero()
    {
        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week, baseline: 2), Sessions(), Sunday);

        Assert.Equal(new[] { "2026-09-14", "2026-09-21", "2026-09-28" }, result.Rows.Select(r => r.Period));
        Assert.Equal(new[] { 3.0, 3.0, 4.0 }, result.Rows.Select(r => r.TotalLoad));
        Assert.Equal(new[] { 7, 7, 7 }, result.Rows.Select(r => r.Days));
        Assert.Equal(3.0 / 7, result.Rows[0].Load, 3);
        Assert.Equal(4.0 / 7, result.Rows[2].Load, 3);
        Assert.Equal(3.0 / 7, result.Rows[2].Baseline!.Value, 3);
    }

    [Fact]
    public void A_day_with_two_sessions_adds_their_loads()
    {
        var sessions = new List<SessionLoadRow> { Row(A, "2026-09-14", 1.0), Row(B, "2026-09-14", 2.5) };

        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Day), sessions, new DateOnly(2026, 9, 14));

        var row = Assert.Single(result.Rows);
        Assert.Equal("2026-09-14", row.Period);
        Assert.Equal(3.5, row.Load, 3);
        Assert.Equal(2, row.SessionCount);
        Assert.Equal(1, row.Days);
    }

    [Fact]
    public void A_month_averages_over_the_days_it_covers_from_the_first_session_to_today()
    {
        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Month), Sessions(), Sunday);

        Assert.Equal(new[] { "2026-09", "2026-10" }, result.Rows.Select(r => r.Period));
        // September counts from the first session (the 14th) to the 30th; October from the 1st to today (the 4th).
        Assert.Equal(new[] { 17, 4 }, result.Rows.Select(r => r.Days));
        Assert.Equal(6.0 / 17, result.Rows[0].Load, 3);
        Assert.Equal(1.0, result.Rows[1].Load, 3);
    }

    [Fact]
    public void The_current_period_is_averaged_over_the_days_so_far_not_the_whole_period()
    {
        var friday = new DateOnly(2026, 10, 2);

        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week), Sessions(), friday);

        var current = result.Rows[^1];
        Assert.Equal("2026-09-28", current.Period);
        Assert.Equal(5, current.Days);
        Assert.Equal(0.8, current.Load, 3);
    }

    [Fact]
    public void Periods_with_no_sessions_are_zero_rather_than_missing()
    {
        var sessions = new List<SessionLoadRow> { Row(A, "2026-09-14", 1.0), Row(D, "2026-10-02", 4.0) };

        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week), sessions, Sunday);

        var middle = result.Rows[1];
        Assert.Equal("2026-09-21", middle.Period);
        Assert.Equal(0, middle.Load);
        Assert.Equal(0, middle.SessionCount);
    }

    [Fact]
    public void Periods_before_from_feed_the_baseline_but_are_not_returned()
    {
        var query = Query(LoadGrouping.Week, baseline: 2, from: "2026-09-28");

        var result = LoadSeriesCalculator.Compute(query, Sessions(), Sunday);

        var row = Assert.Single(result.Rows);
        Assert.Equal("2026-09-28", row.Period);
        Assert.Equal(3.0 / 7, row.Baseline!.Value, 3);
    }

    [Fact]
    public void A_from_part_way_through_a_week_leaves_out_the_earlier_sessions_of_that_week()
    {
        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week, baseline: 1, from: "2026-09-17"), Sessions(), Sunday);

        var first = result.Rows[0];
        Assert.Equal("2026-09-14", first.Period);
        Assert.Equal(4, first.Days);
        Assert.Equal(0, first.SessionCount);
        Assert.Equal(0, first.Load);
    }

    [Fact]
    public void A_to_in_the_middle_ends_the_series_there()
    {
        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week, to: "2026-09-23"), Sessions(), Sunday);

        Assert.Equal(new[] { "2026-09-14", "2026-09-21" }, result.Rows.Select(r => r.Period));
        Assert.Equal(3, result.Rows[1].Days);
    }

    [Fact]
    public void Unrated_distance_is_added_up_per_period()
    {
        var sessions = new List<SessionLoadRow> { Row(A, "2026-09-14", 1.0, unrated: 200), Row(B, "2026-09-16", 2.0, unrated: 300) };

        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week), sessions, new DateOnly(2026, 9, 20));

        Assert.Equal(500, Assert.Single(result.Rows).UnratedDistance);
    }

    [Fact]
    public void Units_list_every_distance_unit_present()
    {
        var sessions = new List<SessionLoadRow> { Row(A, "2026-09-14", 1.0), new(B, DateOnly.Parse("2026-09-16"), 2.0, 0, DistanceUnit.Yards) };

        var result = LoadSeriesCalculator.Compute(Query(LoadGrouping.Week), sessions, Sunday);

        Assert.Equal(new[] { DistanceUnit.Metres, DistanceUnit.Yards }, result.Units);
    }

    [Fact]
    public void No_sessions_is_an_empty_series()
    {
        Assert.Empty(LoadSeriesCalculator.Compute(Query(LoadGrouping.Week), [], Sunday).Rows);
    }

    [Fact]
    public void The_sessions_to_fetch_reach_back_far_enough_for_the_baseline()
    {
        Assert.Equal(new DateOnly(2026, 9, 14), LoadSeriesCalculator.FetchFrom(Query(LoadGrouping.Week, baseline: 2, from: "2026-09-30")));
        Assert.Equal(new DateOnly(2026, 8, 1), LoadSeriesCalculator.FetchFrom(Query(LoadGrouping.Month, baseline: 2, from: "2026-10-15")));
        Assert.Null(LoadSeriesCalculator.FetchFrom(Query(LoadGrouping.Week)));
        Assert.Null(LoadSeriesCalculator.FetchFrom(Query(LoadGrouping.Session, from: "2026-09-30")));
    }

    [Fact]
    public void A_bad_query_is_rejected()
    {
        Assert.Throws<InvalidLoadQueryException>(() => LoadSeriesCalculator.Compute(Query(LoadGrouping.Week, from: "2026-09-30", to: "2026-09-01"), [], Sunday));
        Assert.Throws<InvalidLoadQueryException>(() => LoadSeriesCalculator.Compute(Query(LoadGrouping.Week, baseline: 0), [], Sunday));
        Assert.Throws<InvalidLoadQueryException>(() => LoadSeriesCalculator.Compute(Query(LoadGrouping.Week, baseline: 27), [], Sunday));
    }

    [Fact]
    public void A_day_by_day_series_over_too_long_a_range_is_rejected()
    {
        var query = Query(LoadGrouping.Day, from: "2025-01-01");

        Assert.Throws<InvalidLoadQueryException>(() => LoadSeriesCalculator.Compute(query, Sessions(), Sunday));
    }
}
