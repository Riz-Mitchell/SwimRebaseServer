using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Maps the effort wording one swimmer has defined for themselves. Matches the whole text only, ignoring case and spacing.</summary>
public sealed class PersonalIntensityMapper : IIntensityMapper
{
    public const string PersonalScale = "personal";

    private readonly Dictionary<string, int> _percents = [];

    public PersonalIntensityMapper(IEnumerable<UserEffortTerm> terms)
    {
        foreach (var term in terms)
        {
            _percents[Normalize(term.Phrase)] = term.Percent;
        }
    }

    public static string Normalize(string raw) =>
        string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public bool TryMap(string raw, out Intensity intensity)
    {
        if (_percents.TryGetValue(Normalize(raw), out var percent))
        {
            intensity = new Intensity { Percent = percent, Scale = PersonalScale };
            return true;
        }

        intensity = new Intensity();
        return false;
    }
}
