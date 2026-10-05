using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>One bucket of an <see cref="EffortScale"/>: every intensity from <paramref name="Min"/> to <paramref name="Max"/> percent, inclusive.</summary>
public sealed record EffortBand(string Key, string Label, int Min, int Max);

/// <summary>
/// A named way of grouping intensity: ordered bands that together cover 1-100 percent. Intensity is always stored as a percent; a scale only
/// decides how it is bucketed when it is shown or grouped.
/// </summary>
public sealed record EffortScale(string Id, string Name, IReadOnlyList<EffortBand> Bands)
{
    public const string Unspecified = "unspecified";
    public const string TensId = "tens";
    public const string CustomId = "custom";

    public const int MinBands = 2;
    public const int MaxBands = 12;
    private const int MaxKeyLength = 16;

    /// <summary>Ten bands, "1" to "10". The scale every client read before percentages existed.</summary>
    public static EffortScale Tens { get; } = new(
        TensId,
        "1-10",
        Enumerable.Range(Intensity.Min, Intensity.Max)
            .GroupBy(Intensity.ToLevel)
            .Select(g => new EffortBand(g.Key.ToString(), g.Key.ToString(), g.Min(), g.Max()))
            .ToList());

    /// <summary>The lowest percent to filter from: an explicit percent wins, else the bottom of a tens level (<c>minIntensity=8</c> is 8 and above).</summary>
    public static int? MinPercent(int? percent, int? tensLevel) =>
        percent ?? (tensLevel is { } level ? Tens.Bands[Math.Clamp(level, 1, 10) - 1].Min : null);

    /// <summary>The highest percent to filter to: an explicit percent wins, else the top of a tens level.</summary>
    public static int? MaxPercent(int? percent, int? tensLevel) =>
        percent ?? (tensLevel is { } level ? Tens.Bands[Math.Clamp(level, 1, 10) - 1].Max : null);

    public string Bucket(int? percent) =>
        percent is { } p && Bands.FirstOrDefault(b => p >= b.Min && p <= b.Max) is { } band ? band.Key : Unspecified;

    /// <summary>Position of a band key, with "unspecified" after every band.</summary>
    public int Order(string key)
    {
        for (var i = 0; i < Bands.Count; i++)
        {
            if (Bands[i].Key == key)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>What is wrong with a user's own bands, or an empty list when they make a usable scale.</summary>
    public static List<string> Validate(IReadOnlyList<EffortBand> bands)
    {
        var problems = new List<string>();
        if (bands.Count < MinBands || bands.Count > MaxBands)
        {
            problems.Add($"a custom scale needs {MinBands}-{MaxBands} bands");
            return problems;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedMin = Intensity.Min;
        foreach (var band in bands)
        {
            if (string.IsNullOrWhiteSpace(band.Key) || band.Key.Length > MaxKeyLength)
            {
                problems.Add($"each band needs a key of 1-{MaxKeyLength} characters");
            }
            else if (!seen.Add(band.Key.Trim()) || string.Equals(band.Key.Trim(), Unspecified, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"band key \"{band.Key}\" must be unique and not \"{Unspecified}\"");
            }

            if (band.Min != expectedMin || band.Max < band.Min)
            {
                problems.Add($"band \"{band.Key}\" must start at {expectedMin} and end at or above it, so the bands run in order without gaps or overlaps");
                break;
            }

            expectedMin = band.Max + 1;
        }

        if (problems.Count == 0 && expectedMin != Intensity.Max + 1)
        {
            problems.Add($"the bands must run from {Intensity.Min} to {Intensity.Max}");
        }

        return problems;
    }

    /// <summary>Cleans the user's labels and keys; call after <see cref="Validate"/> passes.</summary>
    public static EffortScale Custom(IEnumerable<EffortBand> bands) => new(
        CustomId,
        "Custom",
        bands.Select(b => new EffortBand(b.Key.Trim(), string.IsNullOrWhiteSpace(b.Label) ? b.Key.Trim() : b.Label.Trim(), b.Min, b.Max)).ToList());
}
