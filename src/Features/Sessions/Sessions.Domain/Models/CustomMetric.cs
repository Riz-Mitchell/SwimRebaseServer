namespace Sessions.Domain.Models;

/// <summary>
/// What a swimmer asks of their training: the filters of a metrics query and the window it is asked over. Intensity bounds are percents of
/// maximum effort, so the metric keeps its meaning if the swimmer later changes their effort scale.
/// </summary>
public sealed record CustomMetricDefinition(
    string Name,
    IReadOnlyList<Stroke>? Strokes,
    IReadOnlyList<SwimMode>? Modes,
    int? MinPercent,
    int? MaxPercent,
    MetricMeasure Measure,
    MetricWindow Window,
    MetricDimension? GroupBy);

/// <summary>A metric a swimmer keeps on their home page.</summary>
public class CustomMetric
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Stroke names; empty means every stroke.</summary>
    public string[] Strokes { get; set; } = [];

    /// <summary>Mode names; empty means every mode.</summary>
    public string[] Modes { get; set; } = [];

    public int? MinPercent { get; set; }

    public int? MaxPercent { get; set; }

    public MetricMeasure Measure { get; set; }

    public MetricWindow Window { get; set; }

    /// <summary>When set, the home page also gets this metric as a series over time (day, week or month).</summary>
    public MetricDimension? GroupBy { get; set; }

    /// <summary>Home-page order, ascending.</summary>
    public int Position { get; set; }

    public void Apply(CustomMetricDefinition definition)
    {
        Name = definition.Name.Trim();
        Strokes = (definition.Strokes ?? []).Distinct().Select(s => s.ToString()).ToArray();
        Modes = (definition.Modes ?? []).Distinct().Select(m => m.ToString()).ToArray();
        MinPercent = definition.MinPercent;
        MaxPercent = definition.MaxPercent;
        Measure = definition.Measure;
        Window = definition.Window;
        GroupBy = definition.GroupBy;
    }

    public CustomMetricDefinition ToDefinition() => new(
        Name,
        Strokes.Select(s => Enum.Parse<Stroke>(s)).ToList(),
        Modes.Select(m => Enum.Parse<SwimMode>(m)).ToList(),
        MinPercent,
        MaxPercent,
        Measure,
        Window,
        GroupBy);
}
