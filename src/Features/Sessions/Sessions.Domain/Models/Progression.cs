namespace Sessions.Domain.Models;

/// <summary>
/// A descend ("Dec 1-4", "Dec A1-TH", "Dec 60-90%"): effort that rises from the first rep to the last of each <see cref="Span"/> reps.
/// <see cref="From"/> and <see cref="To"/> are the board's own wording; when it gives none the intensity is filled from the swimmer's
/// <see cref="ProgressionDefaults"/> when the session is saved, and that value is kept from then on.
/// </summary>
public sealed record Progression
{
    /// <summary>Reps in one ramp ("Dec 1-4" is 4); null ramps the whole set.</summary>
    public int? Span { get; init; }

    public string? From { get; init; }

    public string? To { get; init; }

    public Intensity? FromIntensity { get; init; }

    public Intensity? ToIntensity { get; init; }
}

/// <summary>What a descend counts as when the board names no endpoint: it starts at the item's own effort or <see cref="DefaultEffort"/> and rises to <see cref="DescendTarget"/>.</summary>
public sealed record ProgressionDefaults(int DefaultEffort, int DescendTarget)
{
    /// <summary>The scale stamped on an intensity that came from a default rather than from the board.</summary>
    public const string AssumedScale = "default";

    public static ProgressionDefaults BuiltIn { get; } = new(SessionPreferences.BuiltInDefaultEffort, SessionPreferences.BuiltInDescendTarget);
}
