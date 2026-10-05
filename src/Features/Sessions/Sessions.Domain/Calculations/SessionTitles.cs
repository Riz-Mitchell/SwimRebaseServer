using System.Globalization;
using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Names and cleans session titles. The converter's own title is preferred; <see cref="Build"/> is what a session gets without one.</summary>
public static class SessionTitles
{
    public const int MaxLength = 80;

    /// <summary>Kick, pull, drill or scull must make up at least this share of the distance to be named.</summary>
    private const decimal ModeShare = 0.3m;

    private static readonly Stroke[] ImStrokes = [Stroke.Fly, Stroke.Back, Stroke.Breast, Stroke.Free];

    /// <summary>One line, single spaces, at most <see cref="MaxLength"/> characters; null when nothing is left.</summary>
    public static string? Clean(string? title)
    {
        var cleaned = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength].TrimEnd();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>E.g. <c>Lane 2 - Freestyle and kick - 2,400 m</c>: the group, the main stroke (and mode when it is a real share), and the total.</summary>
    public static string Build(SwimSession session, DistanceUnit unit, DateOnly performedOn)
    {
        var leaves = SessionLeaves.Flatten(session);
        var total = leaves.Sum(l => l.Distance);
        if (total <= 0)
        {
            return $"Swim {performedOn:yyyy-MM-dd}";
        }

        var parts = new List<string>();
        if (Clean(session.Group) is { } group)
        {
            parts.Add(group);
        }

        var strokes = StrokeDistances(leaves);
        var main = strokes.MaxBy(s => s.Value);
        var strokeName = main.Value * 2 > total ? main.Key : "Mixed";

        var modes = leaves
            .Where(l => l.Mode != SwimMode.Swim)
            .GroupBy(l => l.Mode)
            .Select(g => (Mode: g.Key, Distance: g.Sum(l => l.Distance)))
            .OrderByDescending(m => m.Distance)
            .ToList();
        var mode = modes.Sum(m => m.Distance) >= total * ModeShare ? modes[0].Mode.ToString().ToLowerInvariant() : null;

        parts.Add(mode is null ? strokeName : $"{strokeName} and {mode}");
        parts.Add($"{total.ToString("#,##0.##", CultureInfo.InvariantCulture)} {(unit == DistanceUnit.Yards ? "yd" : "m")}");
        return Clean(string.Join(" - ", parts))!;
    }

    // An IM part is flattened to one leaf per stroke sharing a node id; here it counts as IM, not as four strokes.
    private static Dictionary<string, decimal> StrokeDistances(IReadOnlyList<SessionLeaf> leaves)
    {
        var distances = new Dictionary<string, decimal>();
        foreach (var node in leaves.GroupBy(l => l.NodeId))
        {
            var isIm = node.Count() == ImStrokes.Length && ImStrokes.All(s => node.Any(l => l.Stroke == s));
            foreach (var leaf in node)
            {
                var name = isIm ? "IM" : StrokeName(leaf.Stroke);
                distances[name] = distances.GetValueOrDefault(name) + leaf.Distance;
            }
        }

        return distances;
    }

    private static string StrokeName(Stroke stroke) => stroke switch
    {
        Stroke.Free => "Freestyle",
        Stroke.Back => "Backstroke",
        Stroke.Breast => "Breaststroke",
        Stroke.Fly => "Butterfly",
        Stroke.Im => "IM",
        _ => "Mixed",
    };
}
