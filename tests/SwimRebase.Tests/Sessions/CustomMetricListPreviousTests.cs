using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class CustomMetricListPreviousTests
{
    private static readonly Guid Swimmer = Guid.NewGuid();
    private static readonly DateOnly Wednesday = new(2026, 10, 14);

    private static readonly CustomMetricDefinition Fly = new(
        "Fly", [Stroke.Fly], null, null, null, MetricMeasure.Distance, MetricWindow.ThisWeek, null);

    private static readonly FactTotal[] Facts =
    [
        Fact("2026-10-14", Stroke.Fly, 80, 1200),
        Fact("2026-10-05", Stroke.Fly, 90, 1000),
        Fact("2026-10-08", Stroke.Fly, 85, 4000),
        Fact("2026-02-03", Stroke.Fly, 80, 300),
        Fact("2026-02-28", Stroke.Fly, 80, 500),
    ];

    private static FactTotal Fact(string date, Stroke stroke, int intensity, long distance) =>
        new(DateOnly.Parse(date), Guid.NewGuid(), stroke, SwimMode.Swim, intensity, DistanceUnit.Metres, distance);

    private static async Task<CustomMetricValue> Evaluate(CustomMetricDefinition definition, DateOnly today, bool includePrevious = true)
    {
        var repository = new InMemoryCustomMetricRepository();
        var data = new InMemoryMetricsRepository(Facts);
        await new CustomMetricService(repository, new NoOpUnitOfWork()).CreateAsync(Swimmer, definition, default);
        var evaluator = new CustomMetricEvaluator(repository, data, new MetricsService(data, new InMemoryPreferencesRepository()));
        return Assert.Single(await evaluator.EvaluateAsync(Swimmer, today, includePrevious, default));
    }

    [Fact]
    public async Task A_week_is_compared_with_last_week_up_to_the_same_weekday()
    {
        var value = await Evaluate(Fly, Wednesday);

        Assert.Equal(1200, value.Value);
        Assert.Equal(new PreviousFigure(5000, 1000), value.Previous);
    }

    [Fact]
    public async Task A_month_is_compared_up_to_the_same_day_of_the_month()
    {
        var value = await Evaluate(Fly with { Window = MetricWindow.ThisMonth }, new DateOnly(2026, 3, 10));

        Assert.Equal(new PreviousFigure(800, 300), value.Previous);
    }

    [Fact]
    public async Task A_short_previous_month_is_used_whole_when_today_is_past_its_end()
    {
        var value = await Evaluate(Fly with { Window = MetricWindow.ThisMonth }, new DateOnly(2026, 3, 31));

        Assert.Equal(new PreviousFigure(800, 800), value.Previous);
    }

    [Fact]
    public async Task A_rolling_window_is_compared_whole_with_no_to_date_figure()
    {
        var value = await Evaluate(Fly with { Window = MetricWindow.Last7Days }, Wednesday);

        Assert.Equal(new PreviousFigure(1000, null), value.Previous);
    }

    [Fact]
    public async Task All_time_has_no_previous_figure()
    {
        Assert.Null((await Evaluate(Fly with { Window = MetricWindow.AllTime }, Wednesday)).Previous);
    }

    [Fact]
    public async Task Nothing_in_the_previous_period_is_zero_and_an_average_over_nothing_is_not_a_number()
    {
        var distance = await Evaluate(Fly with { Strokes = [Stroke.Breast] }, Wednesday);
        var average = await Evaluate(Fly with { Strokes = [Stroke.Breast], Measure = MetricMeasure.AvgIntensity }, Wednesday);

        Assert.Equal(new PreviousFigure(0, 0), distance.Previous);
        Assert.Equal(new PreviousFigure(null, null), average.Previous);
    }

    [Fact]
    public async Task Without_asking_there_is_no_previous_figure()
    {
        Assert.Null((await Evaluate(Fly, Wednesday, includePrevious: false)).Previous);
    }

    [Theory]
    [InlineData("previous", true)]
    [InlineData(null, false)]
    public void Include_accepts_previous_and_nothing_else(string? include, bool expected)
    {
        Assert.Equal(expected, CustomMetricDetailRequest.ParseInclude(include));
        Assert.Throws<InvalidCustomMetricException>(() => CustomMetricDetailRequest.ParseInclude("everything"));
    }
}
