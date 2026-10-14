using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class CustomMetricTests
{
    private static readonly Guid Swimmer = Guid.NewGuid();
    private static readonly DateOnly Wednesday = new(2026, 10, 14);

    private static readonly CustomMetricDefinition FlyAtThreshold = new(
        "Fly at TH+", [Stroke.Fly], null, 80, null, MetricMeasure.Distance, MetricWindow.ThisWeek, null);

    [Theory]
    [InlineData(MetricWindow.Last7Days, "2026-10-08", "2026-10-14")]
    [InlineData(MetricWindow.Last30Days, "2026-09-15", "2026-10-14")]
    [InlineData(MetricWindow.ThisWeek, "2026-10-12", "2026-10-14")]
    [InlineData(MetricWindow.ThisMonth, "2026-10-01", "2026-10-14")]
    public void A_window_runs_up_to_the_swimmers_today(MetricWindow window, string from, string to)
    {
        var (start, end) = MetricWindows.Resolve(window, Wednesday);

        Assert.Equal(DateOnly.Parse(from), start);
        Assert.Equal(DateOnly.Parse(to), end);
    }

    [Fact]
    public void All_time_has_no_dates()
    {
        var (from, to) = MetricWindows.Resolve(MetricWindow.AllTime, Wednesday);

        Assert.Null(from);
        Assert.Null(to);
    }

    [Fact]
    public async Task A_metric_counts_only_matching_distance_inside_its_window()
    {
        var harness = new Harness();
        await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold, default);

        var value = Assert.Single(await harness.Evaluator.EvaluateAsync(Swimmer, Wednesday, default));

        // Fly at 80%+ this week: 200 on Monday. Not the 50% fly, the freestyle, or last week's fly.
        Assert.Equal(200, value.Value);
        Assert.Null(value.Series);
    }

    [Fact]
    public async Task A_series_reads_the_same_filters_by_period()
    {
        var harness = new Harness();
        await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold with { Window = MetricWindow.AllTime, GroupBy = MetricDimension.Week }, default);

        var value = Assert.Single(await harness.Evaluator.EvaluateAsync(Swimmer, Wednesday, default));

        Assert.Equal(300, value.Value);
        Assert.Equal(new[] { "2026-10-05", "2026-10-12" }, value.Series!.Select(r => r.Keys[0]).ToArray());
        Assert.Equal(new double?[] { 100, 200 }, value.Series!.Select(r => r.Value).ToArray());
    }

    [Fact]
    public async Task A_metric_over_nothing_is_zero_but_an_average_over_nothing_is_not_a_number()
    {
        var harness = new Harness();
        await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold with { Strokes = [Stroke.Breast] }, default);
        await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold with { Strokes = [Stroke.Breast], Measure = MetricMeasure.AvgIntensity }, default);

        var values = await harness.Evaluator.EvaluateAsync(Swimmer, Wednesday, default);

        Assert.Equal(0, values[0].Value);
        Assert.Null(values[1].Value);
    }

    [Fact]
    public async Task Metrics_come_back_in_the_order_they_were_added_and_only_for_their_owner()
    {
        var harness = new Harness();
        await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold with { Name = "first" }, default);
        await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold with { Name = "second" }, default);
        await harness.Metrics.CreateAsync(Guid.NewGuid(), FlyAtThreshold with { Name = "someone else's" }, default);

        var listed = await harness.Metrics.ListAsync(Swimmer, default);

        Assert.Equal(new[] { "first", "second" }, listed.Select(m => m.Definition.Name).ToArray());
    }

    [Fact]
    public async Task Update_and_delete_leave_other_swimmers_metrics_alone()
    {
        var harness = new Harness();
        var mine = await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold, default);
        var stranger = Guid.NewGuid();

        Assert.Null(await harness.Metrics.UpdateAsync(stranger, mine.Id, FlyAtThreshold with { Name = "taken" }, default));
        Assert.False(await harness.Metrics.DeleteAsync(stranger, mine.Id, default));
        Assert.Equal("Renamed", (await harness.Metrics.UpdateAsync(Swimmer, mine.Id, FlyAtThreshold with { Name = " Renamed " }, default))!.Definition.Name);
        Assert.True(await harness.Metrics.DeleteAsync(Swimmer, mine.Id, default));
        Assert.Empty(await harness.Metrics.ListAsync(Swimmer, default));
    }

    [Fact]
    public async Task A_swimmer_cannot_keep_more_than_the_limit()
    {
        var harness = new Harness();
        for (var i = 0; i < CustomMetricService.MaxMetrics; i++)
        {
            await harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold, default);
        }

        await Assert.ThrowsAsync<InvalidCustomMetricException>(() => harness.Metrics.CreateAsync(Swimmer, FlyAtThreshold, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_metric_needs_a_name(string name)
    {
        await Assert.ThrowsAsync<InvalidCustomMetricException>(() => new Harness().Metrics.CreateAsync(Swimmer, FlyAtThreshold with { Name = name }, default));
    }

    [Fact]
    public async Task A_series_can_only_be_over_time()
    {
        await Assert.ThrowsAsync<InvalidCustomMetricException>(
            () => new Harness().Metrics.CreateAsync(Swimmer, FlyAtThreshold with { GroupBy = MetricDimension.Stroke }, default));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(101, null)]
    [InlineData(null, 0)]
    public async Task Intensity_bounds_are_percents(int? min, int? max)
    {
        await Assert.ThrowsAsync<InvalidCustomMetricException>(
            () => new Harness().Metrics.CreateAsync(Swimmer, FlyAtThreshold with { MinPercent = min, MaxPercent = max }, default));
    }

    [Fact]
    public async Task A_lowest_intensity_above_the_highest_is_refused()
    {
        await Assert.ThrowsAsync<InvalidMetricsQueryException>(
            () => new Harness().Metrics.CreateAsync(Swimmer, FlyAtThreshold with { MinPercent = 90, MaxPercent = 80 }, default));
    }

    [Fact]
    public void The_chat_tool_reads_a_metric_the_way_the_api_does()
    {
        var parsed = CustomMetricTool.Parse("""{"name":"Fly","strokes":["fly"],"minPercent":80,"measure":"distance","window":"thisWeek","groupBy":"day"}""");

        Assert.Equal("Fly", parsed.Name);
        Assert.Equal(new[] { Stroke.Fly }, parsed.Strokes!.ToArray());
        Assert.Equal(MetricWindow.ThisWeek, parsed.Window);
        Assert.Equal(MetricDimension.Day, parsed.GroupBy);
        Assert.Throws<InvalidCustomMetricException>(() => CustomMetricTool.Parse("""{"window":"someday"}"""));
    }

    private sealed class Harness
    {
        public Harness()
        {
            var repository = new InMemoryCustomMetricRepository();
            Metrics = new CustomMetricService(repository, new NoOpUnitOfWork());
            Evaluator = new CustomMetricEvaluator(repository, new MetricsService(new FixedMetricsRepository(), new InMemoryPreferencesRepository()));
        }

        public CustomMetricService Metrics { get; }

        public CustomMetricEvaluator Evaluator { get; }
    }

    // The real repository filters in SQL; the calculator applies the same filters, so handing it everything exercises the same rules.
    private sealed class FixedMetricsRepository : IMetricsRepository
    {
        public Task<IReadOnlyList<FactTotal>> GetTotalsAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FactTotal>>(
            [
                Fact("2026-10-12", Stroke.Fly, 80, 200),
                Fact("2026-10-13", Stroke.Fly, 50, 300),
                Fact("2026-10-13", Stroke.Free, 90, 100),
                Fact("2026-10-05", Stroke.Fly, 90, 100),
            ]);

        private static FactTotal Fact(string date, Stroke stroke, int intensity, long distance) =>
            new(DateOnly.Parse(date), Guid.NewGuid(), stroke, SwimMode.Swim, intensity, DistanceUnit.Metres, distance);
    }
}
