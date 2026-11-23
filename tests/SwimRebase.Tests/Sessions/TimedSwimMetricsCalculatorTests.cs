using Sessions.Application.Metrics;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class TimedSwimMetricsCalculatorTests
{
    private static TimedSwimFact Swim(
        string date, decimal dist, double seconds, Stroke stroke = Stroke.Free, int? intensity = null, StartType? start = null,
        FinishReference? finish = null, PoolCourse course = PoolCourse.Scm, SwimMode mode = SwimMode.Swim, params ResistanceKind[] gear) =>
        new(DateOnly.Parse(date), course, dist, stroke, mode, seconds, intensity, start, finish, gear);

    private static TimedSwimMetricsQuery Query(
        TimedSwimMeasure measure = TimedSwimMeasure.Count,
        TimedSwimDimension[]? groupBy = null,
        decimal[]? distances = null,
        Stroke[]? strokes = null,
        StartType[]? startTypes = null,
        ResistanceKind[]? kinds = null,
        bool? hasResistance = null,
        int? min = null,
        int? max = null,
        string? from = null,
        string? to = null,
        EffortScale? scale = null) =>
        new(from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), distances, strokes, null, null, startTypes, null, kinds, hasResistance, min, max, groupBy ?? [], measure, Scale: scale);

    private static List<TimedSwimFact> Swims() =>
    [
        Swim("2026-11-02", 25, 12.4, Stroke.Free, 100, StartType.Dive, FinishReference.Head),
        Swim("2026-11-09", 25, 11.9, Stroke.Free, 100, StartType.Dive, FinishReference.Head),
        Swim("2026-11-09", 25, 13.8, Stroke.Free, 100, StartType.Push, FinishReference.Hand, gear: ResistanceKind.Sponges),
        Swim("2026-11-16", 25, 14.5, Stroke.Free, 90, StartType.Dive, null, gear: [ResistanceKind.Sponges, ResistanceKind.Parachute]),
        Swim("2026-11-16", 50, 27.1, Stroke.Fly, 80, StartType.Push),
        Swim("2026-12-01", 50, 30.0, Stroke.Back, null, null, null, PoolCourse.Scy),
    ];

    private static Dictionary<string, double?> Table(TimedSwimMetricsResult result) =>
        result.Rows.ToDictionary(r => string.Join('|', r.Keys), r => r.Value);

    [Fact]
    public void Counts_swims_with_no_grouping()
    {
        var result = TimedSwimMetricsCalculator.Compute(Query(), Swims());

        Assert.Equal(6, result.TotalSwims);
        Assert.Equal(6, Assert.Single(result.Rows).Value);
        Assert.Equal([DistanceUnit.Metres, DistanceUnit.Yards], result.Units);
    }

    [Fact]
    public void Best_time_by_start_type_for_one_event()
    {
        var result = TimedSwimMetricsCalculator.Compute(
            Query(TimedSwimMeasure.BestTime, [TimedSwimDimension.StartType], distances: [25], strokes: [Stroke.Free]), Swims());

        var table = Table(result);
        Assert.Equal(11.9, table["dive"]);
        Assert.Equal(13.8, table["push"]);
        Assert.False(result.MixedEvents);
        Assert.Equal("dive", result.Rows[0].Keys[0]); // fastest first
    }

    [Fact]
    public void Average_time_is_rounded_and_flags_mixed_events()
    {
        var comparable = TimedSwimMetricsCalculator.Compute(Query(TimedSwimMeasure.AvgTime, distances: [25], strokes: [Stroke.Free], startTypes: [StartType.Dive]), Swims());
        Assert.Equal(12.93, Assert.Single(comparable.Rows).Value);
        Assert.False(comparable.MixedEvents);

        var mixed = TimedSwimMetricsCalculator.Compute(Query(TimedSwimMeasure.AvgTime), Swims());
        Assert.True(mixed.MixedEvents);

        var groupedApart = TimedSwimMetricsCalculator.Compute(Query(TimedSwimMeasure.AvgTime, [TimedSwimDimension.Distance, TimedSwimDimension.Stroke]), Swims());
        Assert.False(groupedApart.MixedEvents);
    }

    [Fact]
    public void A_swim_with_several_kinds_of_gear_counts_under_each_and_ungeared_swims_are_none()
    {
        var table = Table(TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.ResistanceKind]), Swims()));

        Assert.Equal(2, table["sponges"]);
        Assert.Equal(1, table["parachute"]);
        Assert.Equal(4, table["none"]);
    }

    [Fact]
    public void Filters_by_gear_kind_and_by_having_gear()
    {
        Assert.Equal(1, TimedSwimMetricsCalculator.Compute(Query(kinds: [ResistanceKind.Parachute]), Swims()).TotalSwims);
        Assert.Equal(2, TimedSwimMetricsCalculator.Compute(Query(hasResistance: true), Swims()).TotalSwims);
        Assert.Equal(4, TimedSwimMetricsCalculator.Compute(Query(hasResistance: false), Swims()).TotalSwims);
    }

    [Fact]
    public void Groups_by_two_dimensions_with_unspecified_values_kept()
    {
        var table = Table(TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.StartType, TimedSwimDimension.Finish]), Swims()));

        Assert.Equal(2, table["dive|head"]);
        Assert.Equal(1, table["dive|unspecified"]);
        Assert.Equal(1, table["push|hand"]);
        Assert.Equal(1, table["push|unspecified"]);
        Assert.Equal(1, table["unspecified|unspecified"]);
    }

    [Fact]
    public void Distances_read_in_numeric_order_and_months_in_time_order()
    {
        var byDistance = TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.Distance]), [.. Swims(), Swim("2026-11-20", 100, 60), Swim("2026-11-21", 12.5m, 7)]);
        Assert.Equal(["12.5", "25", "50", "100"], byDistance.Rows.Select(r => r.Keys[0]));

        var byMonth = TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.Month]), Swims());
        Assert.Equal(["2026-11", "2026-12"], byMonth.Rows.Select(r => r.Keys[0]));
    }

    [Fact]
    public void Intensity_buckets_with_the_chosen_scale_and_unspecified_last()
    {
        var zones = EffortVocabulary.Default.FindScale("zones")!;
        var result = TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.Intensity], scale: zones), Swims());

        Assert.Equal("Max", result.Rows[^2].Keys[0]);
        Assert.Equal("unspecified", result.Rows[^1].Keys[0]);
    }

    [Fact]
    public void Intensity_bounds_leave_out_swims_with_no_intensity()
    {
        Assert.Equal(4, TimedSwimMetricsCalculator.Compute(Query(min: 90), Swims()).TotalSwims);
        Assert.Equal(1, TimedSwimMetricsCalculator.Compute(Query(max: 80), Swims()).TotalSwims);
    }

    [Fact]
    public void Average_intensity_ignores_swims_without_one_and_is_null_when_none_have_one()
    {
        Assert.Equal(97.5, Assert.Single(TimedSwimMetricsCalculator.Compute(Query(TimedSwimMeasure.AvgIntensity, strokes: [Stroke.Free]), Swims()).Rows).Value);
        Assert.Null(Assert.Single(TimedSwimMetricsCalculator.Compute(Query(TimedSwimMeasure.AvgIntensity, strokes: [Stroke.Back]), Swims()).Rows).Value);
    }

    [Fact]
    public void Date_range_filters_are_inclusive()
    {
        Assert.Equal(4, TimedSwimMetricsCalculator.Compute(Query(from: "2026-11-09", to: "2026-11-16"), Swims()).TotalSwims);
    }

    [Fact]
    public void Bad_queries_are_rejected()
    {
        Assert.Throws<InvalidMetricsQueryException>(() => TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.Stroke, TimedSwimDimension.Mode, TimedSwimDimension.Day]), []));
        Assert.Throws<InvalidMetricsQueryException>(() => TimedSwimMetricsCalculator.Compute(Query(groupBy: [TimedSwimDimension.Stroke, TimedSwimDimension.Stroke]), []));
        Assert.Throws<InvalidMetricsQueryException>(() => TimedSwimMetricsCalculator.Compute(Query(from: "2026-12-01", to: "2026-11-01"), []));
        Assert.Throws<InvalidMetricsQueryException>(() => TimedSwimMetricsCalculator.Compute(Query(min: 90, max: 50), []));
    }
}
