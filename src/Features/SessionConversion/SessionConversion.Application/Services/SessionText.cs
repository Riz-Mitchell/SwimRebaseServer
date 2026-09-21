using System.Text;
using SessionConversion.Domain.Calculations;
using SessionConversion.Domain.Models;

namespace SessionConversion.Application.Services;

/// <summary>
/// Renders a session as compact, numbered text - what the main chat's model reads to talk about a draft and to
/// address edits by path (<c>[2]</c>, or <c>[1.0]</c> for the first item inside the second set).
/// </summary>
public static class SessionText
{
    public static string Outline(SwimSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Group: {session.Group ?? "(not specified / everyone)"}");
        sb.AppendLine($"Distance: {SessionCalculator.Total(session)}");
        for (var i = 0; i < session.Sets.Count; i++)
        {
            AppendNode(sb, session.Sets[i], i.ToString(), indent: 0);
        }

        sb.AppendLine("Breakdown:");
        sb.Append(BreakdownCalculator.Of(session).ToText());
        return sb.ToString().TrimEnd();
    }

    private static void AppendNode(StringBuilder sb, SessionNode node, string path, int indent)
    {
        var pad = new string(' ', indent * 2);
        switch (node)
        {
            case SetGroup group:
                sb.AppendLine($"{pad}[{path}] {group.Repeat} rounds of ({SessionCalculator.Total(node)} total):");
                for (var i = 0; i < group.Items.Count; i++)
                {
                    AppendNode(sb, group.Items[i], $"{path}.{i}", indent + 1);
                }

                break;
            case SetItem item:
                sb.AppendLine($"{pad}[{path}] {Describe(item)}  ({SessionCalculator.Total(node)})");
                break;
        }
    }

    /// <summary>One line for a single set: "8 x 50 fly kick with fins - fast, rest 20s".</summary>
    public static string Describe(SetItem item)
    {
        var sb = new StringBuilder();
        sb.Append(item.Reps).Append(" x ").Append(item.Dist);

        if (item.Strokes.Count > 0)
        {
            sb.Append(' ').Append(string.Join('/', item.Strokes.Select(s => s.ToString().ToLowerInvariant())));
        }

        if (item.Mode != SwimMode.Swim)
        {
            sb.Append(' ').Append(item.Mode.ToString().ToLowerInvariant());
        }

        if (item.Equip.Count > 0)
        {
            sb.Append(" with ").Append(string.Join(", ", item.Equip));
        }

        if (!string.IsNullOrWhiteSpace(item.Effort))
        {
            sb.Append(" - ").Append(item.Effort);
        }

        if (item.Rest is > 0)
        {
            sb.Append(", rest ").Append(item.Rest).Append('s');
        }

        if (item.Parts is { Count: > 0 } parts)
        {
            sb.Append(" [each rep: ").Append(string.Join(" + ", parts.Select(DescribePart))).Append(']');
        }

        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            sb.Append(" (").Append(item.Note).Append(')');
        }

        return sb.ToString();
    }

    private static string DescribePart(Part part)
    {
        var text = part.Dist.ToString();
        if (part.Stroke is { } stroke) text += " " + stroke.ToString().ToLowerInvariant();
        if (part.Mode is { } mode && mode != SwimMode.Swim) text += " " + mode.ToString().ToLowerInvariant();
        return text;
    }
}
