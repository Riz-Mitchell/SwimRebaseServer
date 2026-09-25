using Sessions.Application.Metrics;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class MetricsTests
{
    private static readonly Guid SessionA = Guid.NewGuid();
    private static readonly Guid SessionB = Guid.NewGuid();
    private static readonly Guid SessionC = Guid.NewGuid();

    // Mon 2026-09-14 (A), Wed 2026-09-16 (B), Tue 2026-09-22 (C, the following week), Fri 2026-10-02 (D, next month).
    private static readonly Guid SessionD = Guid.NewGuid();

    private static List<FactTotal> Facts() =>
    [
        Fact(SessionA, "2026-09-14", Stroke.Free, SwimMode.Swim, 2, 400),
        Fact(SessionA, "2026-09-14", Stroke.Fly, SwimMode.Swim, 8, 200),
        Fact(SessionB, "2026-09-16", Stroke.Fly, SwimMode.Kick, 5, 300),
        Fact(SessionB, "2026-09-16", Stroke.Free, SwimMode.Swim, null, 100),
        Fact(SessionC, "2026-09-22", Stroke.Fly, SwimMode.Swim, 9, 100),
        Fact(SessionD, "2026-10-02", Stroke.Back, SwimMode.Swim, 8, 500),
    ];

    private static FactTotal Fact(Guid session, string date, Stroke stroke, SwimMode mode, int? intensity, long distance) =>
        new(DateOnly.Parse(date), session, stroke, mode, intensity, DistanceUnit.Metres, distance);

    private static MetricsQuery Query(
        MetricMeasure measure = MetricMeasure.Distance,
        MetricDimension[]? groupBy = null,
        Stroke[]? strokes = null,
        int? min = null,
        int? max = null,
        string? from = null,
        string? to = null) =>
        new(from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), strokes, null, min, max, groupBy ?? [], measure);

    private static Dictionary<string, double?> Table(MetricsResult result) => result.Rows.ToDictionary(r => string.Join('|', r.Keys), r => r.Value);

    [Fact]
    public void An_ungrouped_query_is_one_row_with_the_total()
    {
        var result = MetricsCalculator.Compute(Query(), Facts());

        var row = Assert.Single(result.Rows);
        Assert.Empty(row.Keys);
        Assert.Equal(1600, row.Value);
        Assert.Equal(1600, result.TotalDistance);
    }

    [Fact]
    public void How_much_fly_this_month_is_a_stroke_filter_over_a_date_range()
    {
        var result = MetricsCalculator.Compute(Query(strokes: [Stroke.Fly], from: "2026-09-01", to: "2026-09-30"), Facts());

        Assert.Equal(600, Assert.Single(result.Rows).Value);
    }

    [Fact]
    public void Grouping_by_stroke_lists_the_biggest_first()
    {
        var result = MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Stroke]), Facts());

        Assert.Equal(new[] { "fly", "back", "free" }, result.Rows.Select(r => r.Keys.Single()));
        Assert.Equal(new Dictionary<string, double?> { ["back"] = 500, ["fly"] = 600, ["free"] = 500 }, Table(result));
    }

    [Fact]
    public void Distance_at_hard_effort_per_week_leaves_out_unspecified_intensity()
    {
        var result = MetricsCalculator.Compute(Query(min: 8, groupBy: [MetricDimension.Week]), Facts());

        // Weeks are named by their Monday and read in date order.
        Assert.Equal(new[] { "2026-09-14", "2026-09-21", "2026-09-28" }, result.Rows.Select(r => r.Keys.Single()));
        Assert.Equal(new double?[] { 200, 100, 500 }, result.Rows.Select(r => r.Value));
    }

    [Fact]
    public void Months_and_days_group_by_calendar_date()
    {
        var byMonth = MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Month]), Facts());
        Assert.Equal(new Dictionary<string, double?> { ["2026-09"] = 1100, ["2026-10"] = 500 }, Table(byMonth));

        var byDay = MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Day], from: "2026-09-16", to: "2026-09-22"), Facts());
        Assert.Equal(new Dictionary<string, double?> { ["2026-09-16"] = 400, ["2026-09-22"] = 100 }, Table(byDay));
    }

    [Fact]
    public void Intensity_distribution_reads_in_numeric_order_with_unspecified_last()
    {
        var result = MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Intensity]), Facts());

        Assert.Equal(new[] { "2", "5", "8", "9", "unspecified" }, result.Rows.Select(r => r.Keys.Single()));
        Assert.Equal(700, Table(result)["8"]);
    }

    [Fact]
    public void Two_dimensions_group_together()
    {
        var result = MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Month, MetricDimension.Stroke], strokes: [Stroke.Fly, Stroke.Free]), Facts());

        Assert.Equal(new Dictionary<string, double?>
        {
            ["2026-09|fly"] = 600,
            ["2026-09|free"] = 500,
        }, Table(result));
    }

    [Fact]
    public void Sessions_are_counted_once_however_many_facts_they_have()
    {
        var overall = MetricsCalculator.Compute(Query(MetricMeasure.Sessions), Facts());
        Assert.Equal(4, Assert.Single(overall.Rows).Value);

        var flySessions = MetricsCalculator.Compute(Query(MetricMeasure.Sessions, strokes: [Stroke.Fly]), Facts());
        Assert.Equal(3, Assert.Single(flySessions.Rows).Value);
    }

    [Fact]
    public void Average_intensity_is_weighted_by_distance_and_ignores_unspecified()
    {
        var result = MetricsCalculator.Compute(Query(MetricMeasure.AvgIntensity, from: "2026-09-14", to: "2026-09-16"), Facts());

        // (400*2 + 200*8 + 300*5) / 900; the 100 with no intensity is left out.
        Assert.Equal(Math.Round((400 * 2 + 200 * 8 + 300 * 5) / 900.0, 2), Assert.Single(result.Rows).Value);
    }

    [Fact]
    public void Average_intensity_has_no_value_when_nothing_had_an_intensity()
    {
        var unspecified = new List<FactTotal> { Fact(SessionA, "2026-09-14", Stroke.Free, SwimMode.Swim, null, 400) };

        Assert.Null(Assert.Single(MetricsCalculator.Compute(Query(MetricMeasure.AvgIntensity), unspecified).Rows).Value);
    }

    [Fact]
    public void An_empty_result_has_no_rows_when_grouped()
    {
        var result = MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Stroke], strokes: [Stroke.Breast]), Facts());

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.TotalDistance);
    }

    [Fact]
    public void The_result_reports_which_distance_units_it_mixes()
    {
        var mixed = new List<FactTotal>
        {
            Fact(SessionA, "2026-09-14", Stroke.Free, SwimMode.Swim, 2, 400),
            new(new DateOnly(2026, 9, 15), SessionB, Stroke.Free, SwimMode.Swim, 2, DistanceUnit.Yards, 200),
        };

        Assert.Equal(new[] { DistanceUnit.Metres, DistanceUnit.Yards }, MetricsCalculator.Compute(Query(), mixed).Units);
        Assert.Contains("mix metres and yards", MetricsTool.Format(MetricsCalculator.Compute(Query(), mixed)));
    }

    [Fact]
    public void Questions_that_cannot_be_answered_are_rejected_with_a_reason()
    {
        Assert.Throws<InvalidMetricsQueryException>(() =>
            MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Day, MetricDimension.Stroke, MetricDimension.Mode]), []));
        Assert.Throws<InvalidMetricsQueryException>(() =>
            MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Stroke, MetricDimension.Stroke]), []));
        Assert.Throws<InvalidMetricsQueryException>(() => MetricsCalculator.Compute(Query(from: "2026-10-01", to: "2026-09-01"), []));
        Assert.Throws<InvalidMetricsQueryException>(() => MetricsCalculator.Compute(Query(min: 9, max: 3), []));
    }

    [Fact]
    public void A_models_question_is_read_from_the_tool_input()
    {
        var query = MetricsTool.Parse("""
            {"from":"2026-09-01","to":"2026-09-30","strokes":["fly"],"minIntensity":8,"groupBy":["week"],"measure":"avgIntensity"}
            """);

        Assert.Equal(new DateOnly(2026, 9, 1), query.From);
        Assert.Equal(new[] { Stroke.Fly }, query.Strokes);
        Assert.Equal(8, query.MinIntensity);
        Assert.Equal(new[] { MetricDimension.Week }, query.GroupBy);
        Assert.Equal(MetricMeasure.AvgIntensity, query.Measure);
    }

    [Fact]
    public void An_empty_question_is_total_distance_and_a_garbled_one_is_rejected()
    {
        var everything = MetricsTool.Parse("{}");
        Assert.Equal(MetricMeasure.Distance, everything.Measure);
        Assert.Empty(everything.GroupBy);

        Assert.Throws<InvalidMetricsQueryException>(() => MetricsTool.Parse("""{"groupBy":["fortnight"]}"""));
    }

    [Fact]
    public void The_answer_is_compact_json_for_the_model()
    {
        var text = MetricsTool.Format(MetricsCalculator.Compute(Query(groupBy: [MetricDimension.Stroke], strokes: [Stroke.Fly]), Facts()));

        Assert.Contains("\"measure\":\"distance\"", text);
        Assert.Contains("\"groupBy\":[\"stroke\"]", text);
        Assert.Contains("\"key\":[\"fly\"],\"value\":600", text);
        Assert.DoesNotContain("note", text);
    }
}
