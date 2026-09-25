using System.Text.Json;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>
/// The wire form of a metrics question - what the chat's model sends and what the REST query string maps onto - and how the answer is
/// written back. Transport-neutral: the Ai feature wraps it in its own tool type.
/// </summary>
public static class MetricsTool
{
    public const string Name = "get_training_metrics";

    public const string Description =
        "Answers how much the user has swum, from their saved sessions: total distance, number of sessions, or average intensity, optionally filtered " +
        "(dates, strokes, modes, intensity range) and grouped by up to 2 of stroke, mode, intensity, day, week (starts Monday), month or session. " +
        "Intensity is 1-10; distance with no stated intensity is \"unspecified\" and is left out when an intensity range is set. " +
        "Use it instead of guessing for questions like \"how much fly this month\" or \"metres at hard effort per week\".";

    public const string Schema = """
        {"type":"object","properties":{"from":{"type":"string","description":"yyyy-MM-dd"},"to":{"type":"string","description":"yyyy-MM-dd"},"strokes":{"type":"array","items":{"type":"string","enum":["free","back","breast","fly","im","choice"]}},"modes":{"type":"array","items":{"type":"string","enum":["swim","kick","pull","drill","scull"]}},"minIntensity":{"type":"integer"},"maxIntensity":{"type":"integer"},"groupBy":{"type":"array","maxItems":2,"items":{"type":"string","enum":["stroke","mode","intensity","day","week","month","session"]}},"measure":{"type":"string","enum":["distance","sessions","avgIntensity"]}}}
        """;

    private sealed record Payload(
        DateOnly? From,
        DateOnly? To,
        List<Stroke>? Strokes,
        List<SwimMode>? Modes,
        int? MinIntensity,
        int? MaxIntensity,
        List<MetricDimension>? GroupBy,
        MetricMeasure? Measure);

    /// <exception cref="InvalidMetricsQueryException">The JSON is not a valid question.</exception>
    public static MetricsQuery Parse(string inputJson)
    {
        try
        {
            var p = SessionJson.Deserialize<Payload>(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            return new MetricsQuery(p.From, p.To, p.Strokes, p.Modes, p.MinIntensity, p.MaxIntensity, p.GroupBy ?? [], p.Measure ?? MetricMeasure.Distance);
        }
        catch (JsonException ex)
        {
            throw new InvalidMetricsQueryException($"could not read the question: {ex.Message}");
        }
    }

    /// <summary>Compact JSON: <c>{"measure":"distance","groupBy":["stroke"],"total":1200,"units":["metres"],"rows":[{"key":["fly"],"value":400}]}</c>.</summary>
    public static string Format(MetricsResult result) => SessionJson.Serialize(new
    {
        measure = result.Measure,
        groupBy = result.GroupBy,
        total = result.TotalDistance,
        units = result.Units,
        note = result.Units.Count > 1 ? "distances mix metres and yards; nothing was converted" : null,
        rows = result.Rows.Select(r => new { key = r.Keys, value = r.Value }),
    });
}
