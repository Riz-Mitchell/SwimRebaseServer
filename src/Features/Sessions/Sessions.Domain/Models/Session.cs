namespace Sessions.Domain.Models;

public enum PoolCourse { Scy, Scm, Lcm }

public enum DistanceUnit { Metres, Yards }

/// <summary>Where a session came from. Provenance, not a lifecycle state.</summary>
public enum SessionSource { Ai, Logged }

/// <summary>
/// A saved swim: a record of what was swum, logged after the fact. The tree is the source of truth; <see cref="SessionFact"/> rows and
/// <see cref="TotalDistance"/> are derived from it on every save.
/// </summary>
public class Session
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public DateOnly PerformedOn { get; set; }

    public string Title { get; set; } = string.Empty;

    public PoolCourse Course { get; set; }

    public SessionSource Source { get; set; }

    /// <summary>The draft this was saved from, if any. Unique, which makes saving the same draft twice return the same session.</summary>
    public Guid? SourceDraftId { get; set; }

    /// <summary>Derived from <see cref="ContentJson"/> by <see cref="Calculations.SessionCalculator"/>.</summary>
    public int TotalDistance { get; set; }

    /// <summary>Derived by <see cref="Calculations.LoadCalculator"/> from the session's facts (and any timed swims counted in its metrics).</summary>
    public double Load { get; set; }

    /// <summary>Distance that had no intensity and was counted in <see cref="Load"/> at the swimmer's default effort.</summary>
    public int UnratedDistance { get; set; }

    /// <summary>A <see cref="SwimSession"/> serialized with <see cref="SessionJson.Options"/>.</summary>
    public string ContentJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DistanceUnit Unit => Course == PoolCourse.Scy ? DistanceUnit.Yards : DistanceUnit.Metres;
}

/// <summary>
/// One row per stroke of one leaf part of a saved session, with repeats already applied - the flat shape every metric is a
/// filter/group/sum over. Rebuilt from the tree on every save (<see cref="Calculations.SessionFactBuilder"/>), never written any other way.
/// </summary>
public class SessionFact
{
    public long Id { get; set; }

    public Guid SessionId { get; set; }

    /// <summary>The part it came from. An IM part produces four rows sharing one node id.</summary>
    public string NodeId { get; set; } = string.Empty;

    public Stroke Stroke { get; set; }

    public SwimMode Mode { get; set; }

    public string[] Equip { get; set; } = [];

    /// <summary>Null = unspecified.</summary>
    public int? Intensity { get; set; }

    /// <summary>Never null: in <see cref="Session.Unit"/>, repeats applied.</summary>
    public int Distance { get; set; }

    // Copied down from the session so metric queries never join back to it.
    public Guid OwnerId { get; set; }

    public DateOnly PerformedOn { get; set; }

    public DistanceUnit Unit { get; set; }

    public PoolCourse Course { get; set; }

    public SessionSource Source { get; set; }
}
