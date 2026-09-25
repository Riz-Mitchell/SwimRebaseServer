using System.Text;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Services;

/// <summary>
/// Renders a session as compact, numbered text - what the main chat's model reads to talk about a session and to
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

    /// <summary>One line for a single set: "8 x 50 fly kick with fins - fast (8/10), rest 20s"; a mixed rep lists its parts: "4 x 100 (25 fly + 75 free), send-off 1:30".</summary>
    public static string Describe(SetItem item)
    {
        var sb = new StringBuilder();
        sb.Append(item.Reps).Append(" x ");
        sb.Append(item.Parts.Count == 1 ? DescribePart(item.Parts[0]) : $"{item.RepDistance} ({string.Join(" + ", item.Parts.Select(DescribePart))})");

        if (item.Timing is { } timing)
        {
            sb.Append(timing.Kind == TimingKind.SendOff ? ", on " : ", rest ").Append(Seconds(timing.Seconds));
        }

        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            sb.Append(" (").Append(item.Note).Append(')');
        }

        return sb.ToString();
    }

    private static string DescribePart(Part part)
    {
        var sb = new StringBuilder();
        sb.Append(part.Dist);
        sb.Append(' ').Append((part.Stroke ?? Stroke.Choice).ToString().ToLowerInvariant());

        if (part.Mode != SwimMode.Swim)
        {
            sb.Append(' ').Append(part.Mode.ToString().ToLowerInvariant());
        }

        if (part.Equip.Count > 0)
        {
            sb.Append(" with ").Append(string.Join(", ", part.Equip));
        }

        if (!string.IsNullOrWhiteSpace(part.Effort) || part.Intensity is not null)
        {
            sb.Append(" - ").Append(part.Effort ?? "effort");
            sb.Append(part.Intensity is { } i ? $" ({i.Level}/{Intensity.Max})" : " (intensity unspecified)");
        }

        return sb.ToString();
    }

    private static string Seconds(int seconds) => seconds >= 60 ? $"{seconds / 60}:{seconds % 60:00}" : $"{seconds}s";
}
