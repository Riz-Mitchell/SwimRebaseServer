using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class CustomMetricBandsTests
{
    private static readonly Guid Swimmer = Guid.NewGuid();
    private static readonly DateOnly ServerToday = new(2026, 10, 14);

    private static readonly FactTotal[] Facts =
    [
        Fact("2026-10-12", Stroke.Fly, 90, 200),
        Fact("2026-10-12", Stroke.Free, 95, 100),
        Fact("2026-10-12", Stroke.Free, 85, 300),
        Fact("2026-10-13", Stroke.Free, 99, 400),
        Fact("2026-10-13", Stroke.Free, null, 150),
        Fact("2026-10-13", Stroke.Free, 60, 500),
    ];

    private static FactTotal Fact(string date, Stroke stroke, int? intensity, long distance) =>
        new(DateOnly.Parse(date), Guid.NewGuid(), stroke, SwimMode.Swim, intensity, DistanceUnit.Metres, distance);

    private static CustomMetricDefinition Free(MetricMeasure measure = MetricMeasure.Distance, MetricDimension? groupBy = MetricDimension.Day) =>
        new("Free above sub max", [Stroke.Free], null, 80, null, measure, MetricWindow.ThisWeek, groupBy);

    private static CustomMetricDetailRequest Ask(string? thenBy, string? scale = "zones") =>
        CustomMetricDetailRequest.Parse("2026-10-14", null, null, ServerToday, thenBy, scale);

    private static async Task<CustomMetricDetail> Detail(CustomMetricDefinition definition, CustomMetricDetailRequest request)
    {
        var repository = new InMemoryCustomMetricRepository();
        var data = new InMemoryMetricsRepository(Facts);
        var saved = await new CustomMetricService(repository, new NoOpUnitOfWork()).CreateAsync(Swimmer, definition, default);
        var evaluator = new CustomMetricEvaluator(repository, data, new MetricsService(data, new InMemoryPreferencesRepository()));
        return (await evaluator.EvaluateOneAsync(Swimmer, saved.Id, request, default))!;
    }

    [Fact]
    public async Task Each_day_is_split_into_bands_in_scale_order_with_the_scale_attached()
    {
        var detail = await Detail(Free(), Ask("intensity"));

        Assert.All(detail.Series!, r => Assert.Equal(2, r.Keys.Count));
        var days = detail.Series!.Select(r => r.Keys[0]).Distinct().ToArray();
        Assert.Equal(new[] { "2026-10-12", "2026-10-13" }, days);
        var scale = detail.Scale!;
        Assert.Equal("zones", scale.Id);
        foreach (var day in days)
        {
            var order = detail.Series!.Where(r => r.Keys[0] == day).Select(r => scale.Order(r.Keys[1])).ToArray();
            Assert.Equal(order.Order().ToArray(), order);
        }
    }

    [Fact]
    public async Task The_metrics_own_filters_apply_to_every_band_row()
    {
        var detail = await Detail(Free(), Ask("intensity"));

        Assert.Equal(detail.Value, detail.Series!.Sum(r => r.Value));
        Assert.Equal(800, detail.Value);
    }

    [Fact]
    public async Task Distance_with_no_stated_effort_lands_in_the_default_band_and_is_reported()
    {
        var detail = await Detail(Free() with { MinPercent = null }, Ask("intensity"));

        Assert.Equal(150, detail.AssumedDistance);
        Assert.Equal(50, detail.DefaultEffort);
        Assert.Contains(detail.Series!, r => r.Keys[0] == "2026-10-13" && r.Keys[1] == detail.Scale!.Bucket(50));
    }

    [Fact]
    public async Task Without_thenBy_the_response_is_as_before()
    {
        var detail = await Detail(Free(), Ask(null));

        Assert.All(detail.Series!, r => Assert.Single(r.Keys));
        Assert.Null(detail.Scale);
        Assert.Null(detail.DefaultEffort);
    }

    [Theory]
    [InlineData(MetricMeasure.Sessions)]
    [InlineData(MetricMeasure.AvgIntensity)]
    public async Task Bands_are_ignored_for_a_measure_other_than_distance(MetricMeasure measure)
    {
        var detail = await Detail(Free(measure), Ask("intensity"));

        Assert.All(detail.Series!, r => Assert.Single(r.Keys));
        Assert.Null(detail.Scale);
    }

    [Fact]
    public async Task Bands_are_ignored_for_a_metric_with_no_series()
    {
        var detail = await Detail(Free(groupBy: null), Ask("intensity"));

        Assert.Null(detail.Series);
        Assert.Null(detail.Scale);
    }

    [Fact]
    public async Task An_unknown_scale_is_refused()
    {
        await Assert.ThrowsAsync<InvalidMetricsQueryException>(() => Detail(Free(), Ask("intensity", "nonsense")));
    }

    [Theory]
    [InlineData("stroke")]
    [InlineData("day")]
    [InlineData("bogus")]
    [InlineData("1")]
    public void Only_intensity_may_split_a_bar(string thenBy)
    {
        Assert.Throws<InvalidCustomMetricException>(() => Ask(thenBy));
    }

    [Fact]
    public void Intensity_is_read_in_any_case()
    {
        Assert.Equal(MetricDimension.Intensity, Ask("INTENSITY").ThenBy);
    }
}
