namespace Training.Domain.Models;

/// <summary>
/// A basic, AI-generated expectation for a Swim-type <see cref="TimetableEntry"/> version - not a
/// set-by-set plan, just enough to remind the athlete what the session is roughly meant to be.
/// Shares its primary key with the owning <see cref="TimetableEntry"/> (zero-or-one relationship).
/// </summary>
public class ExpectedSession
{
    public Guid TimetableEntryId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public int TotalDistanceMeters { get; set; }

    public EffortZone PrimaryEffortZone { get; set; }

    public TimetableEntry? TimetableEntry { get; set; }
}
