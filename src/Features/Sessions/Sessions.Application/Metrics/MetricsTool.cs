using System.Text.Json;
using Sessions.Domain.Calculations;
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
        "(dates, one session, strokes, modes, intensity range) and grouped by up to 2 of stroke, mode, intensity, day, week (starts Monday), month or session. " +
        "Intensity is a percent of maximum effort (1-100); minPercent/maxPercent filter on it, and the intensity grouping uses the effort scale " +
        "named by scale (tens = bands 1-10, zones = A0, A1, A2, A3, TH, VO2, Sub Max, Max, custom = the user's own), else the user's saved one. " +
        "Distance with no stated intensity is counted at the swimmer's default effort unless applyDefault is false (then it is \"unspecified\" and left out when an intensity range is set); the answer reports how much was assumed. " +
        "Use it instead of guessing for questions like \"how much fly this month\" or \"metres at hard effort per week\".";

    public const string Schema = """
        {"type":"object","properties":{"sessionId":{"type":"string","description":"limit to one saved session (guid)"},"from":{"type":"string","description":"yyyy-MM-dd"},"to":{"type":"string","description":"yyyy-MM-dd"},"strokes":{"type":"array","items":{"type":"string","enum":["free","back","breast","fly","im","choice"]}},"modes":{"type":"array","items":{"type":"string","enum":["swim","kick","pull","drill","scull"]}},"minPercent":{"type":"integer","description":"1-100"},"maxPercent":{"type":"integer","description":"1-100"},"scale":{"type":"string","enum":["tens","zones","custom"]},"groupBy":{"type":"array","maxItems":2,"items":{"type":"string","enum":["stroke","mode","intensity","day","week","month","session"]}},"measure":{"type":"string","enum":["distance","sessions","avgIntensity"]},"applyDefault":{"type":"boolean","description":"count distance with no stated effort at the swimmer's default effort (default true)"}}}
        """;

    private sealed record Payload(
        Guid? SessionId,
        DateOnly? From,
        DateOnly? To,
        List<Stroke>? Strokes,
        List<SwimMode>? Modes,
        int? MinPercent,
        int? MaxPercent,
        int? MinIntensity,
        int? MaxIntensity,
        string? Scale,
        List<MetricDimension>? GroupBy,
        MetricMeasure? Measure,
        bool? ApplyDefault);

    /// <exception cref="InvalidMetricsQueryException">The JSON is not a valid question.</exception>
    public static MetricsQuery Parse(string inputJson)
    {
        try
        {
            var p = SessionJson.Deserialize<Payload>(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            // minIntensity/maxIntensity are the earlier 1-10 filter; a level covers the percents of its tens band.
            return new MetricsQuery(
                p.From, p.To, p.Strokes, p.Modes,
                EffortScale.MinPercent(p.MinPercent, p.MinIntensity), EffortScale.MaxPercent(p.MaxPercent, p.MaxIntensity),
                p.GroupBy ?? [], p.Measure ?? MetricMeasure.Distance, p.Scale, SessionId: p.SessionId, ApplyDefaultEffort: p.ApplyDefault ?? true);
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
        scale = result.Scale.Id,
        defaultEffort = result.DefaultEffort,
        assumedDistance = result.AssumedDistance,
        note = result.Units.Count > 1 ? "distances mix metres and yards; nothing was converted" : null,
        assumedNote = result.AssumedDistance > 0 ? $"{result.AssumedDistance} of the distance had no stated effort and was counted at {result.DefaultEffort}%" : null,
        rows = result.Rows.Select(r => new { key = r.Keys, value = r.Value }),
    });
}
