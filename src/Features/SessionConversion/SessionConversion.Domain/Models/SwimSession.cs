using System.Text.Json.Serialization;

namespace SessionConversion.Domain.Models;

public enum Stroke { Free, Back, Breast, Fly, Im, Choice }

public enum SwimMode { Swim, Kick, Pull, Drill, Scull }

/// <summary>
/// What one swimmer actually swam, in order: only the path they took through a workout board. There are no ids, sections,
/// confidence scores, source lines or stated totals - either/or sets are already resolved by the interview and distances are
/// always computed (see <see cref="Calculations.SessionCalculator"/>), never taken from the model.
/// </summary>
public sealed record SwimSession
{
    /// <summary>The group/lane the swimmer answered as; null when the board is for everyone.</summary>
    public string? Group { get; init; }

    public List<SessionNode> Sets { get; init; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(SetItem), "item")]
[JsonDerivedType(typeof(SetGroup), "group")]
public abstract record SessionNode;

/// <summary>A leaf: "8 x 50 kick @ 1:00". Ladders are written as one item per rung.</summary>
public sealed record SetItem : SessionNode
{
    public int Reps { get; init; } = 1;

    /// <summary>Distance of each rep.</summary>
    public int Dist { get; init; }

    public List<Stroke> Strokes { get; init; } = [];

    public SwimMode Mode { get; init; } = SwimMode.Swim;

    public List<string> Equip { get; init; } = [];

    /// <summary>Effort/pace text as written ("easy", "build", "fast").</summary>
    public string? Effort { get; init; }

    /// <summary>Rest or send-off in seconds.</summary>
    public int? Rest { get; init; }

    /// <summary>Ordered parts of ONE rep when a rep mixes strokes/modes/equipment/effort. Distances add up to <see cref="Dist"/>.</summary>
    public List<Part>? Parts { get; init; }

    public string? Note { get; init; }
}

public sealed record Part
{
    public int Dist { get; init; }

    public Stroke? Stroke { get; init; }

    public SwimMode? Mode { get; init; }

    public List<string>? Equip { get; init; }

    public string? Effort { get; init; }
}

/// <summary>"3 rounds of ...": repeats its items <see cref="Repeat"/> times.</summary>
public sealed record SetGroup : SessionNode
{
    public int Repeat { get; init; } = 1;

    public List<SessionNode> Items { get; init; } = [];
}
