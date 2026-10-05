using System.Text.Json;

namespace Sessions.Domain.Calculations;

/// <summary>An effort phrase and the percent it stands for. <see cref="Scale"/> is the tag stored on the intensity: "words" or "zones".</summary>
public sealed record EffortEntry(string Scale, int Percent, IReadOnlyList<string> Phrases);

/// <summary>
/// Every effort word and zone name a board can use, and the named scales intensity can be shown on, read from one embedded file. The mapper
/// reads it to turn text into a percent, the conversion prompt lists it for the model, and metrics read the scales - so they cannot disagree.
/// </summary>
public sealed class EffortVocabulary
{
    public const string WordsScale = "words";
    public const string ZonesScale = "zones";

    private const string ResourceName = "effort-vocabulary.json";

    private static readonly Lazy<EffortVocabulary> Loaded = new(Load);

    private sealed record FileShape(List<EffortEntry> Entries, List<SystemShape> Systems);

    private sealed record SystemShape(string Id, string Name, List<EffortBand> Bands);

    private EffortVocabulary(IReadOnlyList<EffortEntry> entries, IReadOnlyList<EffortScale> systems)
    {
        Entries = entries;
        Scales = [EffortScale.Tens, .. systems];
    }

    public static EffortVocabulary Default => Loaded.Value;

    public IReadOnlyList<EffortEntry> Entries { get; }

    /// <summary>The built-in scales: the tens scale first, then the ones in the file.</summary>
    public IReadOnlyList<EffortScale> Scales { get; }

    public EffortScale? FindScale(string? id) => Scales.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The vocabulary as the conversion prompt states it: terms easiest to hardest, then the zone names.</summary>
    public string PromptText()
    {
        var terms = Entries
            .Where(e => e.Scale == WordsScale)
            .OrderBy(e => e.Percent)
            .SelectMany(e => e.Phrases);
        var zones = Scales.FirstOrDefault(s => s.Id == ZonesScale)?.Bands.Select(b => b.Key) ?? [];
        return $"Effort terms, easiest to hardest: {string.Join(", ", terms)}. Zones: {string.Join(", ", zones)}.";
    }

    private static EffortVocabulary Load()
    {
        using var stream = typeof(EffortVocabulary).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var shape = JsonSerializer.Deserialize<FileShape>(stream, options) ?? throw new InvalidOperationException($"{ResourceName} is empty.");
        var systems = shape.Systems.Select(s => new EffortScale(s.Id, s.Name, s.Bands)).ToList();

        foreach (var system in systems)
        {
            var problems = EffortScale.Validate(system.Bands);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException($"Effort scale \"{system.Id}\" in {ResourceName} is invalid: {string.Join("; ", problems)}.");
            }
        }

        return new EffortVocabulary(shape.Entries, systems);
    }
}
