using System.Text;
using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Distance swum, grouped four ways.</summary>
public sealed record SessionBreakdown
{
    public const string Unspecified = "unspecified";

    public int Total { get; init; }

    public Dictionary<string, int> ByStroke { get; init; } = [];

    public Dictionary<string, int> ByMode { get; init; } = [];

    /// <summary>A rep with two pieces of equipment counts towards both, so these can add up to more than <see cref="Total"/>. "none" = no equipment.</summary>
    public Dictionary<string, int> ByEquipment { get; init; } = [];

    /// <summary>Keyed by level on the global scale ("1".."10"), or "unspecified".</summary>
    public Dictionary<string, int> ByIntensity { get; init; } = [];

    public string ToText()
    {
        var sb = new StringBuilder();

        void Line(string title, Dictionary<string, int> values) =>
            sb.AppendLine($"  {title,-10} " + string.Join(", ", values.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));

        sb.AppendLine($"  total      {Total}");
        Line("stroke", ByStroke);
        Line("mode", ByMode);
        Line("equipment", ByEquipment);
        Line("intensity", ByIntensity);
        return sb.ToString();
    }
}

/// <summary>A group-by over <see cref="SessionLeaves"/>.</summary>
public static class BreakdownCalculator
{
    public static SessionBreakdown Of(SwimSession session)
    {
        var stroke = new Dictionary<string, int>();
        var mode = new Dictionary<string, int>();
        var equipment = new Dictionary<string, int>();
        var intensity = new Dictionary<string, int>();
        var total = 0;

        foreach (var leaf in SessionLeaves.Flatten(session))
        {
            total += leaf.Distance;
            Add(stroke, leaf.Stroke.ToString().ToLowerInvariant(), leaf.Distance);
            Add(mode, leaf.Mode.ToString().ToLowerInvariant(), leaf.Distance);
            Add(intensity, leaf.Intensity?.ToString() ?? SessionBreakdown.Unspecified, leaf.Distance);

            var gear = leaf.Equip.Select(Normalize).Where(g => g.Length > 0).Distinct().ToList();
            if (gear.Count == 0)
            {
                Add(equipment, "none", leaf.Distance);
            }

            foreach (var g in gear)
            {
                Add(equipment, g, leaf.Distance);
            }
        }

        return new SessionBreakdown { Total = total, ByStroke = stroke, ByMode = mode, ByEquipment = equipment, ByIntensity = intensity };
    }

    private static void Add(Dictionary<string, int> values, string key, int dist) => values[key] = values.GetValueOrDefault(key) + dist;

    private static string Normalize(string? text) => string.Join(' ', (text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
