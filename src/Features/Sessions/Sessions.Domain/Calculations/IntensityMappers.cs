using System.Text.RegularExpressions;
using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Maps effort as written on a board, or entered by a swimmer, onto the global <see cref="Intensity"/> scale. One implementation per external scale.</summary>
public interface IIntensityMapper
{
    /// <summary>False when the text is not on this mapper's scale, or is ambiguous ("easy/hard").</summary>
    bool TryMap(string raw, out Intensity intensity);
}

/// <summary>Everyday effort words ("easy", "build", "race pace") and percentages ("80%").</summary>
public sealed partial class WordsIntensityMapper : IIntensityMapper
{
    public const string WordsScale = "words";
    public const string PercentScale = "percent";

    // Most specific first: a phrase is removed from the text once it matches, so "very easy" is not also read as "easy".
    private static readonly (string Phrase, int Level)[] Words =
    [
        ("very easy", 1), ("very hard", 9),
        ("all out", 10), ("max", 10), ("sprint", 10),
        ("race pace", 9), ("race", 9),
        ("hard", 8), ("fast", 8),
        ("strong", 7), ("threshold", 7),
        ("moderate", 5), ("medium", 5), ("build", 5),
        ("steady", 4), ("aerobic", 4),
        ("smooth", 3),
        ("easy", 2), ("ez", 2), ("recovery", 2), ("relaxed", 2), ("loosen", 2),
    ];

    public bool TryMap(string raw, out Intensity intensity)
    {
        intensity = new Intensity();
        var text = raw.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return false;
        }

        if (PercentPattern().Match(text) is { Success: true } percent && text.Length == percent.Length)
        {
            var value = int.Parse(percent.Groups[1].Value);
            intensity = new Intensity { Level = Math.Clamp((int)Math.Round(value / 10.0, MidpointRounding.AwayFromZero), Intensity.Min, Intensity.Max), Scale = PercentScale };
            return true;
        }

        var levels = new HashSet<int>();
        foreach (var (phrase, level) in Words)
        {
            var pattern = new Regex($@"\b{Regex.Escape(phrase)}\b");
            if (pattern.IsMatch(text))
            {
                levels.Add(level);
                text = pattern.Replace(text, " ");
            }
        }

        if (levels.Count != 1)
        {
            return false;
        }

        intensity = new Intensity { Level = levels.Single(), Scale = WordsScale };
        return true;
    }

    [GeneratedRegex(@"^(\d{1,3})\s*%$")]
    private static partial Regex PercentPattern();
}
