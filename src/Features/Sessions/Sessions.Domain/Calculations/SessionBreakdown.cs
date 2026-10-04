using System.Text;
using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Distance swum, grouped four ways.</summary>
public sealed record SessionBreakdown
{
    public const string Unspecified = "unspecified";

    public decimal Total { get; init; }

    public Dictionary<string, decimal> ByStroke { get; init; } = [];

    public Dictionary<string, decimal> ByMode { get; init; } = [];

    /// <summary>A rep with two pieces of equipment counts towards both, so these can add up to more than <see cref="Total"/>. "none" = no equipment.</summary>
    public Dictionary<string, decimal> ByEquipment { get; init; } = [];

    /// <summary>Keyed by level on the global scale ("1".."10"), or "unspecified".</summary>
    public Dictionary<string, decimal> ByIntensity { get; init; } = [];

    public string ToText()
    {
        var sb = new StringBuilder();

        void Line(string title, Dictionary<string, decimal> values) =>
            sb.AppendLine($"  {title,-10} " + string.Join(", ", values.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value:0.##}")));

        sb.AppendLine($"  total      {Total:0.##}");
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
        var stroke = new Dictionary<string, decimal>();
        var mode = new Dictionary<string, decimal>();
        var equipment = new Dictionary<string, decimal>();
        var intensity = new Dictionary<string, decimal>();
        var total = 0m;

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

    private static void Add(Dictionary<string, decimal> values, string key, decimal dist) => values[key] = values.GetValueOrDefault(key) + dist;

    private static string Normalize(string? text) => string.Join(' ', (text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
