using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>The measures a piece of resistance gear can carry.</summary>
[Flags]
public enum ResistanceMeasure
{
    None = 0,
    Count = 1,
    WidthCm = 2,
    HeightCm = 4,
    LengthCm = 8,
    WeightKg = 16,
    TensionLevel = 32,
    Note = 64,
}

/// <summary>
/// What each <see cref="ResistanceKind"/> measures: which measures it must have and which it may have. Adding common gear is one
/// line here, so the rest of the pipeline (storage, validation, metrics) needs no change.
/// </summary>
public static class ResistanceCatalog
{
    public const int MaxItemsPerSwim = 10;
    public const int MaxNoteLength = 160;

    public sealed record Spec(ResistanceMeasure Required, ResistanceMeasure Allowed);

    public static IReadOnlyDictionary<ResistanceKind, Spec> Specs { get; } = new Dictionary<ResistanceKind, Spec>
    {
        [ResistanceKind.Sponges] = new(ResistanceMeasure.Count, ResistanceMeasure.Count | ResistanceMeasure.Note),
        [ResistanceKind.Parachute] = new(ResistanceMeasure.WidthCm | ResistanceMeasure.HeightCm, ResistanceMeasure.WidthCm | ResistanceMeasure.HeightCm | ResistanceMeasure.Note),
        [ResistanceKind.StretchCord] = new(ResistanceMeasure.LengthCm, ResistanceMeasure.LengthCm | ResistanceMeasure.TensionLevel | ResistanceMeasure.Note),
        [ResistanceKind.DragSuit] = new(ResistanceMeasure.None, ResistanceMeasure.Note),
        [ResistanceKind.WeightedBelt] = new(ResistanceMeasure.WeightKg, ResistanceMeasure.WeightKg | ResistanceMeasure.Note),
        [ResistanceKind.DragBucket] = new(ResistanceMeasure.None, ResistanceMeasure.Note),
        [ResistanceKind.Other] = new(ResistanceMeasure.Note, ResistanceMeasure.Note),
    };

    /// <summary>Problems a re-ask can fix, one per issue, each naming the item it is about.</summary>
    public static List<string> Validate(IReadOnlyList<ResistanceItem> resistances)
    {
        var errors = new List<string>();
        if (resistances.Count > MaxItemsPerSwim)
        {
            errors.Add($"resistances can have at most {MaxItemsPerSwim} items");
        }

        for (var i = 0; i < resistances.Count; i++)
        {
            var item = resistances[i];
            var path = $"resistances[{i}]";
            if (!Specs.TryGetValue(item.Kind, out var spec))
            {
                errors.Add($"{path}: unknown kind {item.Kind}");
                continue;
            }

            var present = Present(item);
            foreach (var missing in Names(spec.Required & ~present))
            {
                errors.Add($"{path}: {item.Kind} needs {missing}");
            }

            foreach (var extra in Names(present & ~spec.Allowed))
            {
                errors.Add($"{path}: {item.Kind} does not use {extra}");
            }

            if (item.Count is <= 0) errors.Add($"{path}: count must be > 0");
            if (item.WidthCm is <= 0) errors.Add($"{path}: widthCm must be > 0");
            if (item.HeightCm is <= 0) errors.Add($"{path}: heightCm must be > 0");
            if (item.LengthCm is <= 0) errors.Add($"{path}: lengthCm must be > 0");
            if (item.WeightKg is <= 0) errors.Add($"{path}: weightKg must be > 0");
            if (item.TensionLevel is < 1 or > 10) errors.Add($"{path}: tensionLevel must be between 1 and 10");
            if (item.Note is { Length: > MaxNoteLength }) errors.Add($"{path}: note can be at most {MaxNoteLength} characters");
        }

        return errors;
    }

    private static ResistanceMeasure Present(ResistanceItem item)
    {
        var present = ResistanceMeasure.None;
        if (item.Count is not null) present |= ResistanceMeasure.Count;
        if (item.WidthCm is not null) present |= ResistanceMeasure.WidthCm;
        if (item.HeightCm is not null) present |= ResistanceMeasure.HeightCm;
        if (item.LengthCm is not null) present |= ResistanceMeasure.LengthCm;
        if (item.WeightKg is not null) present |= ResistanceMeasure.WeightKg;
        if (item.TensionLevel is not null) present |= ResistanceMeasure.TensionLevel;
        if (!string.IsNullOrWhiteSpace(item.Note)) present |= ResistanceMeasure.Note;
        return present;
    }

    private static IEnumerable<string> Names(ResistanceMeasure measures) =>
        Enum.GetValues<ResistanceMeasure>().Where(m => m != ResistanceMeasure.None && measures.HasFlag(m)).Select(m => char.ToLowerInvariant(m.ToString()[0]) + m.ToString()[1..]);
}
