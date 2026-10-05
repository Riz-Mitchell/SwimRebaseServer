using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>One stroke of one part with every enclosing repeat applied.</summary>
/// <param name="NodeId">The part's id, or its position (<c>1.0.2</c>: set 1, item 0, part 2) for a session that has not been saved yet.</param>
public sealed record SessionLeaf(string NodeId, Stroke Stroke, SwimMode Mode, IReadOnlyList<string> Equip, int? Intensity, decimal Distance);

/// <summary>
/// Flattens the tree into leaves. The single place repeats are multiplied out: facts and breakdowns are both built from this, so they cannot
/// disagree with each other or with <see cref="SessionCalculator"/>.
/// </summary>
public static class SessionLeaves
{
    private static readonly Stroke[] ImOrder = [Stroke.Fly, Stroke.Back, Stroke.Breast, Stroke.Free];

    public static List<SessionLeaf> Flatten(SwimSession session)
    {
        var leaves = new List<SessionLeaf>();
        Walk(session.Sets, string.Empty, multiplier: 1, leaves);
        return leaves;
    }

    private static void Walk(List<SessionNode> nodes, string path, int multiplier, List<SessionLeaf> leaves)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var here = path.Length == 0 ? i.ToString() : $"{path}.{i}";
            switch (nodes[i])
            {
                case SetGroup group:
                    Walk(group.Items, here, multiplier * Math.Max(group.Repeat, 0), leaves);
                    break;
                case SetItem item:
                    for (var p = 0; p < item.Parts.Count; p++)
                    {
                        AddPart(item.Parts[p], $"{here}.{p}", Math.Max(item.Reps, 0) * multiplier, leaves);
                    }

                    break;
            }
        }
    }

    private static void AddPart(Part part, string position, int times, List<SessionLeaf> leaves)
    {
        var distance = part.Dist * times;
        if (distance <= 0)
        {
            return;
        }

        var nodeId = part.Id ?? position;
        var stroke = part.Stroke ?? Stroke.Choice;
        var equip = part.Equip;
        var intensity = part.Intensity?.Percent;

        if (stroke != Stroke.Im)
        {
            leaves.Add(new SessionLeaf(nodeId, stroke, part.Mode, equip, intensity, distance));
            return;
        }

        // An IM part is a quarter of each stroke in order (any remainder, which validation rejects, goes to free).
        var quarter = distance / 4;
        for (var k = 0; k < 4; k++)
        {
            leaves.Add(new SessionLeaf(nodeId, ImOrder[k], part.Mode, equip, intensity, k == 3 ? distance - 3 * quarter : quarter));
        }
    }
}
