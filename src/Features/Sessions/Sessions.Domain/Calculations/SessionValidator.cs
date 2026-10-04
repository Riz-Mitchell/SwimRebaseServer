using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

public static class SessionValidator
{
    /// <summary>Problems a re-ask can fix: impossible numbers, missing strokes, groups nested too deep, empty groups.</summary>
    public static List<string> Validate(SwimSession session)
    {
        var errors = new List<string>();
        if (session.Sets.Count == 0)
        {
            errors.Add("sets is empty");
        }

        for (var i = 0; i < session.Sets.Count; i++)
        {
            Check(session.Sets[i], $"sets[{i}]", errors, groupDepth: 0);
        }

        return errors;
    }

    private static void Check(SessionNode node, string path, List<string> errors, int groupDepth)
    {
        switch (node)
        {
            case SetGroup group:
                if (groupDepth + 1 > SessionRules.MaxGroupDepth)
                {
                    errors.Add($"{path}: groups nest more than {SessionRules.MaxGroupDepth} deep - flatten it");
                    return;
                }

                if (group.Repeat < 1) errors.Add($"{path}: repeat must be >= 1");
                if (group.Items.Count == 0) errors.Add($"{path}: group has no items");
                for (var i = 0; i < group.Items.Count; i++)
                {
                    Check(group.Items[i], $"{path}.items[{i}]", errors, groupDepth + 1);
                }

                break;
            case SetItem item:
                if (item.Reps < 1) errors.Add($"{path}: reps must be >= 1");
                if (item.Timing is { Seconds: < 0 }) errors.Add($"{path}: timing seconds cannot be negative");
                if (item.Parts.Count == 0) errors.Add($"{path}: item has no parts - give it at least one part with a dist and a stroke");
                for (var p = 0; p < item.Parts.Count; p++)
                {
                    CheckPart(item.Parts[p], $"{path}.parts[{p}]", errors);
                }

                break;
        }
    }

    private static void CheckPart(Part part, string path, List<string> errors)
    {
        if (part.Dist < 1) errors.Add($"{path}: dist must be >= 1");
        if (decimal.Round(part.Dist, 2) != part.Dist) errors.Add($"{path}: dist can have at most 2 decimal places");
        if (part.Stroke is null) errors.Add($"{path}: stroke is required (use \"choice\" if the swimmer picked freely)");
        if (part.Stroke == Stroke.Im && part.Dist % 4 != 0) errors.Add($"{path}: an IM part is a quarter of each stroke, so dist must divide by 4");
        if (part.Intensity is { } i && (i.Level < Intensity.Min || i.Level > Intensity.Max))
        {
            errors.Add($"{path}: intensity must be {Intensity.Min}-{Intensity.Max}");
        }
    }
}
