using System.Text;
using SessionConversion.Domain.Models;

namespace SessionConversion.Domain.Calculations;

/// <summary>Distance swum, grouped four ways.</summary>
public sealed record SessionBreakdown
{
    public int Total { get; init; }

    public Dictionary<string, int> ByStroke { get; init; } = [];

    public Dictionary<string, int> ByMode { get; init; } = [];

    /// <summary>A rep with two pieces of equipment counts towards both, so these can add up to more than <see cref="Total"/>. "none" = no equipment.</summary>
    public Dictionary<string, int> ByEquipment { get; init; } = [];

    public Dictionary<string, int> ByEffort { get; init; } = [];

    /// <summary>Distance whose stroke could not be pinned down (several strokes listed for a rep with no parts).</summary>
    public int UnresolvedStroke { get; init; }

    public string ToText()
    {
        var sb = new StringBuilder();

        void Line(string title, Dictionary<string, int> values) =>
            sb.AppendLine($"  {title,-10} " + string.Join(", ", values.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));

        sb.AppendLine($"  total      {Total}");
        Line("stroke", ByStroke);
        Line("mode", ByMode);
        Line("equipment", ByEquipment);
        Line("effort", ByEffort);
        return sb.ToString();
    }
}

public static class BreakdownCalculator
{
    private static readonly string[] ImOrder = ["fly", "back", "breast", "free"];

    public static SessionBreakdown Of(SwimSession session)
    {
        var acc = new Accumulator();
        foreach (var node in session.Sets)
        {
            Walk(node, 1, acc);
        }

        return new SessionBreakdown
        {
            Total = acc.Total,
            ByStroke = acc.Stroke,
            ByMode = acc.Mode,
            ByEquipment = acc.Equipment,
            ByEffort = acc.Effort,
            UnresolvedStroke = acc.Unresolved,
        };
    }

    private sealed class Accumulator
    {
        public int Total;
        public int Unresolved;
        public Dictionary<string, int> Stroke = [];
        public Dictionary<string, int> Mode = [];
        public Dictionary<string, int> Equipment = [];
        public Dictionary<string, int> Effort = [];
    }

    private static void Walk(SessionNode node, int multiplier, Accumulator acc)
    {
        switch (node)
        {
            case SetGroup group:
                foreach (var child in group.Items)
                {
                    Walk(child, multiplier * Math.Max(group.Repeat, 0), acc);
                }

                break;
            case SetItem item:
                AddItem(item, multiplier, acc);
                break;
        }
    }

    private static void AddItem(SetItem item, int multiplier, Accumulator acc)
    {
        var reps = Math.Max(item.Reps, 0);

        if (item.Parts is { Count: > 0 } parts)
        {
            foreach (var part in parts)
            {
                AddLeg(item, part.Dist * reps * multiplier, part.Stroke, part.Mode ?? item.Mode, part.Equip ?? item.Equip, part.Effort ?? item.Effort, acc);
            }

            return;
        }

        AddLeg(item, reps * item.Dist * multiplier, null, item.Mode, item.Equip, item.Effort, acc);
    }

    private static void AddLeg(SetItem item, int dist, Stroke? ownStroke, SwimMode mode, List<string> equipment, string? effort, Accumulator acc)
    {
        if (dist <= 0)
        {
            return;
        }

        acc.Total += dist;
        Add(acc.Mode, mode.ToString().ToLowerInvariant(), dist);
        Add(acc.Effort, Normalize(effort) is { Length: > 0 } e ? e : "unspecified", dist);

        var gear = equipment.Select(Normalize).Where(g => g.Length > 0).Distinct().ToList();
        if (gear.Count == 0)
        {
            Add(acc.Equipment, "none", dist);
        }
        else
        {
            foreach (var g in gear)
            {
                Add(acc.Equipment, g, dist);
            }
        }

        var stroke = ownStroke ?? (item.Strokes.Count == 1 ? item.Strokes[0] : (Stroke?)null);
        if (stroke == Stroke.Im)
        {
            // A plain IM rep is 25 of each stroke in order (a quarter each; any remainder goes to free).
            var quarter = dist / 4;
            for (var k = 0; k < 4; k++)
            {
                Add(acc.Stroke, ImOrder[k], k == 3 ? dist - 3 * quarter : quarter);
            }
        }
        else if (stroke is { } s)
        {
            Add(acc.Stroke, s.ToString().ToLowerInvariant(), dist);
        }
        else
        {
            Add(acc.Stroke, item.Strokes.Count > 1 ? "mixed" : "unspecified", dist);
            acc.Unresolved += dist;
        }
    }

    private static void Add(Dictionary<string, int> values, string key, int dist) => values[key] = values.GetValueOrDefault(key) + dist;

    private static string Normalize(string? text) => string.Join(' ', (text ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
