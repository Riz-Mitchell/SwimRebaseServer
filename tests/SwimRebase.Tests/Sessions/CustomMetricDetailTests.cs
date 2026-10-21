using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class CustomMetricDetailTests
{
    private static readonly Guid Swimmer = Guid.NewGuid();
    private static readonly DateOnly ServerToday = new(2026, 10, 14);

    private static readonly CustomMetricDefinition FlyAtThreshold = new(
        "Fly at TH+", [Stroke.Fly], null, 80, null, MetricMeasure.Distance, MetricWindow.ThisWeek, null);

    // Weeks of Mon 5 Oct and Mon 12 Oct 2026, plus September.
    private static readonly FactTotal[] Facts =
    [
        Fact("2026-10-12", Stroke.Fly, 80, 200),
        Fact("2026-10-13", Stroke.Fly, 50, 300),
        Fact("2026-10-13", Stroke.Free, 90, 100),
        Fact("2026-10-05", Stroke.Fly, 90, 100),
        Fact("2026-10-08", Stroke.Fly, 85, 150),
        Fact("2026-09-20", Stroke.Fly, 80, 400),
    ];

    private static FactTotal Fact(string date, Stroke stroke, int intensity, long distance) =>
        new(DateOnly.Parse(date), Guid.NewGuid(), stroke, SwimMode.Swim, intensity, DistanceUnit.Metres, distance);

    private static CustomMetricDetailRequest Ask(string today, string? window = null, string? groupBy = null, string? include = null) =>
        CustomMetricDetailRequest.Parse(today, window, groupBy, ServerToday, include: include);

    private static async Task<(CustomMetricService Metrics, CustomMetricEvaluator Evaluator, Guid Id)> Saved(
        CustomMetricDefinition definition, DateOnly? firstSession = null)
    {
        var repository = new InMemoryCustomMetricRepository();
        var data = new InMemoryMetricsRepository(Facts, firstSession);
        var metrics = new CustomMetricService(repository, new NoOpUnitOfWork());
        var evaluator = new CustomMetricEvaluator(repository, data, new MetricsService(data, new InMemoryPreferencesRepository()));
        return (metrics, evaluator, (await metrics.CreateAsync(Swimmer, definition, default)).Id);
    }

    [Fact]
    public async Task Overriding_the_window_and_grouping_answers_differently_but_saves_nothing()
    {
        var (metrics, evaluator, id) = await Saved(FlyAtThreshold);

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "thisMonth", "week"), default))!;

        Assert.Equal(MetricWindow.ThisMonth, detail.Window);
        Assert.Equal(450, detail.Value);
        Assert.Equal(new[] { "2026-10-05", "2026-10-12" }, detail.Series!.Select(r => r.Keys[0]).ToArray());
        Assert.Equal(new double?[] { 250, 200 }, detail.Series!.Select(r => r.Value).ToArray());

        var stored = Assert.Single(await metrics.ListAsync(Swimmer, default)).Definition;
        Assert.Equal(MetricWindow.ThisWeek, stored.Window);
        Assert.Null(stored.GroupBy);
    }

    [Fact]
    public async Task The_first_session_date_comes_with_every_period()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold, new DateOnly(2026, 9, 1));

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14"), default))!;

        Assert.Equal(new DateOnly(2026, 9, 1), detail.FirstSessionDate);
    }

    [Fact]
    public async Task A_swimmer_with_no_sessions_has_no_first_session_date()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        Assert.Null((await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14"), default))!.FirstSessionDate);
    }

    [Fact]
    public async Task The_previous_period_is_the_week_before_and_only_when_asked_for()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        var without = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", groupBy: "day"), default))!;
        var with = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", groupBy: "day", include: "previous"), default))!;

        Assert.Null(without.Previous);
        var previous = with.Previous!;
        Assert.Equal(new DateRangeView(new(2026, 10, 5), new(2026, 10, 11)), previous.Range);
        Assert.Equal(250, previous.Value);
        Assert.Equal(new[] { "2026-10-05", "2026-10-08" }, previous.Series!.Select(r => r.Keys[0]).ToArray());
        Assert.Equal(200, with.Value);
    }

    [Fact]
    public async Task The_previous_month_is_the_calendar_month_before()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "thisMonth", include: "previous"), default))!;

        Assert.Equal(new DateRangeView(new(2026, 9, 1), new(2026, 9, 30)), detail.Previous!.Range);
        Assert.Equal(400, detail.Previous.Value);
    }

    [Fact]
    public async Task All_time_has_no_previous_period()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "allTime", include: "previous"), default))!;

        Assert.Null(detail.Previous);
    }

    [Fact]
    public void An_unknown_include_is_refused()
    {
        Assert.Throws<InvalidCustomMetricException>(() => Ask("2026-10-14", include: "next"));
    }

    [Fact]
    public async Task An_earlier_anchor_gives_that_weeks_value_and_daily_series()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-07", groupBy: "day"), default))!;

        Assert.Equal(250, detail.Value);
        Assert.Equal(new[] { "2026-10-05", "2026-10-08" }, detail.Series!.Select(r => r.Keys[0]).ToArray());
        Assert.Equal(new DateRangeView(new(2026, 10, 5), new(2026, 10, 11)), detail.Range);
    }

    [Theory]
    [InlineData("thisWeek", "2026-10-14", "2026-10-12", "2026-10-18")]
    [InlineData("thisMonth", "2026-10-14", "2026-10-01", "2026-10-31")]
    [InlineData("thisMonth", "2026-02-10", "2026-02-01", "2026-02-28")]
    [InlineData("thisMonth", "2028-02-10", "2028-02-01", "2028-02-29")]
    [InlineData("last7Days", "2026-10-14", "2026-10-08", "2026-10-14")]
    [InlineData("last30Days", "2026-10-14", "2026-09-15", "2026-10-14")]
    public async Task The_range_is_the_whole_period_the_window_covers(string window, string today, string from, string to)
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask(today, window), default))!;

        Assert.Equal(new DateRangeView(DateOnly.Parse(from), DateOnly.Parse(to)), detail.Range);
    }

    [Theory]
    [InlineData("2026-09-20", "2026-10-14", "2026-09-20", 850d)]
    [InlineData(null, "2026-10-14", "2026-10-14", 850d)]
    [InlineData("2026-09-20", "2026-09-10", "2026-09-10", 0d)]
    [InlineData("2026-09-20", "2026-09-30", "2026-09-20", 400d)]
    public async Task All_time_runs_from_the_first_session_up_to_the_anchor(string? first, string today, string from, double value)
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold, first is null ? null : DateOnly.Parse(first));

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask(today, "allTime"), default))!;

        Assert.Equal(DateOnly.Parse(from), detail.Range.From);
        Assert.Equal(DateOnly.Parse(today), detail.Range.To);
        Assert.Equal(value, detail.Value);
    }

    [Fact]
    public async Task No_saved_grouping_and_no_override_means_no_series()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        Assert.Null((await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14"), default))!.Series);
    }

    [Fact]
    public async Task A_saved_grouping_is_used_when_nothing_overrides_it()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold with { GroupBy = MetricDimension.Day });

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14"), default))!;

        Assert.Equal(new[] { "2026-10-12" }, detail.Series!.Select(r => r.Keys[0]).ToArray());
    }

    [Fact]
    public async Task Another_swimmers_metric_and_an_unknown_id_are_not_found()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold);

        Assert.Null(await evaluator.EvaluateOneAsync(Guid.NewGuid(), id, Ask("2026-10-14"), default));
        Assert.Null(await evaluator.EvaluateOneAsync(Swimmer, Guid.NewGuid(), Ask("2026-10-14"), default));
    }

    [Fact]
    public async Task Each_bars_average_intensity_is_its_own_not_a_running_figure()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold with { MinPercent = null, Measure = MetricMeasure.AvgIntensity });

        var detail = (await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "thisMonth", "week"), default))!;

        // Week of 5 Oct: (90*100 + 85*150) / 250. Week of 12 Oct: (80*200 + 50*300) / 500.
        Assert.Equal(new double?[] { 87, 62 }, detail.Series!.Select(r => r.Value).ToArray());
        Assert.Equal(70.33, detail.Value!.Value, 2);
    }

    [Fact]
    public async Task Nothing_matching_is_zero_but_an_average_over_nothing_is_null()
    {
        var (_, distance, distanceId) = await Saved(FlyAtThreshold with { Strokes = [Stroke.Breast] });
        var (_, average, averageId) = await Saved(FlyAtThreshold with { Strokes = [Stroke.Breast], Measure = MetricMeasure.AvgIntensity });

        Assert.Equal(0, (await distance.EvaluateOneAsync(Swimmer, distanceId, Ask("2026-10-14"), default))!.Value);
        Assert.Null((await average.EvaluateOneAsync(Swimmer, averageId, Ask("2026-10-14"), default))!.Value);
    }

    [Fact]
    public async Task A_series_with_too_many_bars_is_refused_from_the_dates_alone()
    {
        var (_, evaluator, id) = await Saved(FlyAtThreshold, new DateOnly(2024, 1, 1));

        var error = await Assert.ThrowsAsync<InvalidCustomMetricException>(
            () => evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "allTime", "day"), default));

        Assert.Contains("366", error.Message);
        Assert.NotNull(await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "allTime", "month"), default));
        Assert.NotNull(await evaluator.EvaluateOneAsync(Swimmer, id, Ask("2026-10-14", "last30Days", "day"), default));
    }
}
