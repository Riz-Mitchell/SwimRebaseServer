using SessionConversion.Domain.Models;

namespace SessionConversion.Domain.Calculations;

public static class SessionValidator
{
    private const int MaxDepth = 6;

    /// <summary>Problems a re-ask can fix: impossible numbers, parts that do not add up, empty groups.</summary>
    public static List<string> Validate(SwimSession session)
    {
        var errors = new List<string>();
        if (session.Sets.Count == 0)
        {
            errors.Add("sets is empty");
        }

        for (var i = 0; i < session.Sets.Count; i++)
        {
            Check(session.Sets[i], $"sets[{i}]", errors, depth: 0);
        }

        return errors;
    }

    private static void Check(SessionNode node, string path, List<string> errors, int depth)
    {
        if (depth > MaxDepth)
        {
            errors.Add($"{path}: nested too deeply");
            return;
        }

        switch (node)
        {
            case SetGroup group:
                if (group.Repeat < 1) errors.Add($"{path}: repeat must be >= 1");
                if (group.Items.Count == 0) errors.Add($"{path}: group has no items");
                for (var i = 0; i < group.Items.Count; i++)
                {
                    Check(group.Items[i], $"{path}.items[{i}]", errors, depth + 1);
                }

                break;
            case SetItem item:
                if (item.Reps < 1) errors.Add($"{path}: reps must be >= 1");
                if (item.Dist < 1) errors.Add($"{path}: dist must be >= 1");
                if (item.Parts is { Count: > 0 } && item.Parts.Sum(p => p.Dist) != item.Dist)
                {
                    errors.Add($"{path}: parts add up to {item.Parts.Sum(p => p.Dist)} but dist is {item.Dist}");
                }

                break;
        }
    }
}
