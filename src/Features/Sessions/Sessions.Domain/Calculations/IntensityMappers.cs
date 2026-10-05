using System.Text.RegularExpressions;
using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Maps effort as written on a board, or entered by a swimmer, onto the global <see cref="Intensity"/> scale. One implementation per external scale.</summary>
public interface IIntensityMapper
{
    /// <summary>False when the text is not on this mapper's scale, or is ambiguous ("easy/hard").</summary>
    bool TryMap(string raw, out Intensity intensity);
}

/// <summary>Effort words ("easy", "sub max"), zone names ("A2", "TH") and percentages ("80%"), as listed in <see cref="EffortVocabulary"/>.</summary>
public sealed partial class VocabularyIntensityMapper : IIntensityMapper
{
    public const string PercentScale = "percent";

    private readonly (Regex Pattern, EffortEntry Entry)[] _phrases;

    public VocabularyIntensityMapper() : this(EffortVocabulary.Default)
    {
    }

    public VocabularyIntensityMapper(EffortVocabulary vocabulary)
    {
        // Longest phrase first: a phrase is removed from the text once it matches, so "sub max" is not also read as "max".
        _phrases = vocabulary.Entries
            .SelectMany(entry => entry.Phrases.Select(phrase => (Phrase: phrase.ToLowerInvariant(), Entry: entry)))
            .OrderByDescending(p => p.Phrase.Length)
            .Select(p => (new Regex($@"\b{Regex.Escape(p.Phrase)}\b", RegexOptions.Compiled), p.Entry))
            .ToArray();
    }

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
            intensity = new Intensity { Percent = Math.Clamp(value, Intensity.Min, Intensity.Max), Scale = PercentScale };
            return true;
        }

        var matched = new List<EffortEntry>();
        foreach (var (pattern, entry) in _phrases)
        {
            if (pattern.IsMatch(text))
            {
                matched.Add(entry);
                text = pattern.Replace(text, " ");
            }
        }

        if (matched.Select(e => e.Percent).Distinct().Count() != 1)
        {
            return false;
        }

        intensity = new Intensity
        {
            Percent = matched[0].Percent,
            Scale = matched.Any(e => e.Scale == EffortVocabulary.WordsScale) ? EffortVocabulary.WordsScale : matched[0].Scale,
        };
        return true;
    }

    [GeneratedRegex(@"^(\d{1,3})\s*%$")]
    private static partial Regex PercentPattern();
}
