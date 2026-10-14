using System.Text.Json;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>The wire form of "keep this metric on my home page" - what the chat's model sends - and how the saved metric is written back. Transport-neutral, like <see cref="MetricsTool"/>.</summary>
public static class CustomMetricTool
{
    public const string Name = "add_custom_metric";

    public const string Description =
        "Saves a metric to the user's home page, kept up to date as they train: a total (distance, number of sessions, or average intensity) of the swimming that matches " +
        "the filters, over a window (last7Days, last30Days, thisWeek, thisMonth, allTime). Intensity is a percent of maximum effort (1-100): to mean a zone, pass the " +
        "lower edge of that zone as minPercent (and its upper edge as maxPercent for 'only in that zone'). Set groupBy to day, week or month to also draw it as a " +
        "series over time. Use get_training_metrics first when the user wants to see a number rather than keep it.";

    public const string Schema = """
        {"type":"object","required":["name"],"properties":{"name":{"type":"string","description":"short label for the tile, e.g. Fly at TH+ this week"},"strokes":{"type":"array","items":{"type":"string","enum":["free","back","breast","fly","im","choice"]}},"modes":{"type":"array","items":{"type":"string","enum":["swim","kick","pull","drill","scull"]}},"minPercent":{"type":"integer","description":"1-100"},"maxPercent":{"type":"integer","description":"1-100"},"measure":{"type":"string","enum":["distance","sessions","avgIntensity"]},"window":{"type":"string","enum":["last7Days","last30Days","thisWeek","thisMonth","allTime"]},"groupBy":{"type":"string","enum":["day","week","month"]}}}
        """;

    /// <exception cref="InvalidCustomMetricException">The JSON is not a metric.</exception>
    public static CustomMetricDefinition Parse(string inputJson)
    {
        try
        {
            return SessionJson.Deserialize<CustomMetricDefinition>(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidCustomMetricException($"could not read the metric: {ex.Message}");
        }
    }

    public static string Format(CustomMetricView saved) => SessionJson.Serialize(new { id = saved.Id, saved.Definition });
}
