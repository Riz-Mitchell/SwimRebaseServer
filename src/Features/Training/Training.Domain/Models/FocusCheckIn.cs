namespace Training.Domain.Models;

/// <summary>
/// Marks a single calendar day as honored for a <see cref="Focus"/> - at most one per
/// (<see cref="FocusId"/>, <see cref="Date"/>), even if the user swims more than once that day.
/// </summary>
public class FocusCheckIn
{
    public Guid Id { get; set; }

    public Guid FocusId { get; set; }

    public DateOnly Date { get; set; }

    public DateTimeOffset CheckedInAt { get; set; }

    public Focus? Focus { get; set; }
}
