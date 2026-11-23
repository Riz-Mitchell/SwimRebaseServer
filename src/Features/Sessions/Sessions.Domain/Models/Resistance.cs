namespace Sessions.Domain.Models;

/// <summary>The kinds of resistance gear a swim can be done with. Each kind's measures are defined in <see cref="Calculations.ResistanceCatalog"/>.</summary>
public enum ResistanceKind { Sponges, Parachute, StretchCord, DragSuit, WeightedBelt, DragBucket, Other }

/// <summary>
/// One piece of resistance gear as it goes over the wire and back: a kind plus the measures that kind uses (sponge count, parachute
/// width and height, ...). Measures are metric; the client converts for display. Which measures a kind allows or requires is checked
/// by <see cref="Calculations.ResistanceCatalog.Validate"/>.
/// </summary>
public sealed record ResistanceItem
{
    public ResistanceKind Kind { get; init; }

    public int? Count { get; init; }

    public decimal? WidthCm { get; init; }

    public decimal? HeightCm { get; init; }

    public decimal? LengthCm { get; init; }

    public decimal? WeightKg { get; init; }

    /// <summary>A 1-10 feel for how stiff a stretch cord was, when the swimmer tracks that.</summary>
    public int? TensionLevel { get; init; }

    public string? Note { get; init; }

    /// <summary>A parachute's area, derived from its width and height; null for anything without both.</summary>
    public decimal? AreaCm2 => WidthCm is { } w && HeightCm is { } h ? w * h : null;

    public static ResistanceItem Of(TimedSwimResistance r) => new()
    {
        Kind = r.Kind, Count = r.Count, WidthCm = r.WidthCm, HeightCm = r.HeightCm, LengthCm = r.LengthCm,
        WeightKg = r.WeightKg, TensionLevel = r.TensionLevel, Note = r.Note,
    };
}

/// <summary>The stored form of a <see cref="ResistanceItem"/>: a child row of a <see cref="TimedSwim"/>, so metrics can filter and group by kind in SQL.</summary>
public class TimedSwimResistance
{
    public Guid Id { get; set; }

    public Guid TimedSwimId { get; set; }

    public ResistanceKind Kind { get; set; }

    public int? Count { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WeightKg { get; set; }

    public int? TensionLevel { get; set; }

    public string? Note { get; set; }

    public static TimedSwimResistance From(Guid timedSwimId, ResistanceItem item) => new()
    {
        Id = Guid.NewGuid(), TimedSwimId = timedSwimId, Kind = item.Kind, Count = item.Count, WidthCm = item.WidthCm,
        HeightCm = item.HeightCm, LengthCm = item.LengthCm, WeightKg = item.WeightKg, TensionLevel = item.TensionLevel,
        Note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
    };
}
