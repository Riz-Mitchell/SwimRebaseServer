using System.Text.Json.Serialization;

namespace Sessions.Domain.Models;

public enum Stroke { Free, Back, Breast, Fly, Im, Choice }

public enum SwimMode { Swim, Kick, Pull, Drill, Scull }

public enum TimingKind { Rest, SendOff }

/// <summary>Limits of the session tree.</summary>
public static class SessionRules
{
    /// <summary>How deep groups may nest: a group directly in <see cref="SwimSession.Sets"/> is depth 1. Items do not count.</summary>
    public const int MaxGroupDepth = 3;
}

/// <summary>
/// What one swimmer actually swam, in order: only the path they took through a workout. Sections, confidence scores and stated
/// totals are not part of it - distances are always computed (see <see cref="Calculations.SessionCalculator"/>), never taken from the model.
/// </summary>
public sealed record SwimSession
{
    /// <summary>The version a session is written with: 2 stores intensity as a percent (version 1 stored a 1-10 level).</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Bumped when the shape of the stored tree changes, so old sessions can be upgraded when read.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The group/lane the swimmer answered as; null when the board is for everyone.</summary>
    public string? Group { get; init; }

    /// <summary>
    /// A short name for the workout, written by the converter. It rides along in a draft until the session is saved; a saved session keeps
    /// its title in <see cref="Session.Title"/> and stores this as null.
    /// </summary>
    public string? Title { get; init; }

    public List<SessionNode> Sets { get; init; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(SetItem), "item")]
[JsonDerivedType(typeof(SetGroup), "group")]
public abstract record SessionNode
{
    /// <summary>Stable id, assigned when the session is saved (<see cref="Calculations.NodeIds"/>). Never written by the model.</summary>
    public string? Id { get; init; }
}

/// <summary>"8 x 50 @ 1:00": one rep is the ordered <see cref="Parts"/>, swum <see cref="Reps"/> times. Ladders are written as one item per rung.</summary>
public sealed record SetItem : SessionNode
{
    public int Reps { get; init; } = 1;

    public Timing? Timing { get; init; }

    /// <summary>What ONE rep is made of. A plain "8 x 50 kick" is a single part; "25 fly + 75 free" is two.</summary>
    public List<Part> Parts { get; init; } = [];

    public string? Note { get; init; }

    /// <summary>Distance of one rep.</summary>
    [JsonIgnore]
    public decimal RepDistance => Parts.Sum(p => p.Dist);
}

/// <summary>Rest between reps, or the send-off interval they leave on.</summary>
public sealed record Timing
{
    public TimingKind Kind { get; init; }

    public int Seconds { get; init; }
}

/// <summary>The only leaf, and what a fact row is built from: one stroke, one mode, one intensity.</summary>
public sealed record Part
{
    public string? Id { get; init; }

    /// <summary>Distance of this part in one rep.</summary>
    public decimal Dist { get; init; }

    /// <summary>Required. "choice" when the swimmer picked freely; "im" is a quarter each of fly, back, breast, free.</summary>
    public Stroke? Stroke { get; init; }

    public SwimMode Mode { get; init; } = SwimMode.Swim;

    public List<string> Equip { get; init; } = [];

    /// <summary>Effort as written or said ("easy", "80%", "hard"). Mapped to <see cref="Intensity"/> when the session is prepared.</summary>
    public string? Effort { get; init; }

    /// <summary>On the global scale; null means unspecified.</summary>
    public Intensity? Intensity { get; init; }
}

/// <summary>"3 rounds of ...": repeats its items <see cref="Repeat"/> times.</summary>
public sealed record SetGroup : SessionNode
{
    public int Repeat { get; init; } = 1;

    public List<SessionNode> Items { get; init; } = [];
}
