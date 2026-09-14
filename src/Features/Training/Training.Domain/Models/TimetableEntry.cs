namespace Training.Domain.Models;

/// <summary>
/// One version of a recurring weekly slot. Editing a slot never mutates this row - the current
/// version's <see cref="EffectiveTo"/> is closed off and a new row is inserted with the same
/// <see cref="SlotId"/>, so the timetable as it stood on any past date can always be reconstructed.
/// </summary>
public class TimetableEntry
{
    public Guid Id { get; set; }

    /// <summary>Stable across edits - identifies "the same" recurring slot through its whole version history.</summary>
    public Guid SlotId { get; set; }

    public Guid UserId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public SessionType SessionType { get; set; }

    public string? Label { get; set; }

    /// <summary>First calendar date this version governs.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Last calendar date this version governs - null means still active.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public TrainingUser? TrainingUser { get; set; }

    /// <summary>Set only when <see cref="SessionType"/> is <see cref="Training.Domain.Models.SessionType.Swim"/>.</summary>
    public ExpectedSession? ExpectedSession { get; set; }
}
