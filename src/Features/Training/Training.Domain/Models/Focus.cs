namespace Training.Domain.Models;

/// <summary>
/// Something a user is deliberately trying to focus on across their next few swim sessions.
/// <see cref="Priority"/> is a dense rank (1..N, no gaps) among a user's <see cref="FocusStatus.Active"/>
/// focuses - it decides which focus leads pre-session reminder notifications - and is maintained by
/// <c>FocusService</c>, never set directly.
/// </summary>
public class Focus
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TagLine { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    /// <summary>What the user actually asked for, verbatim - only set when <see cref="Origin"/> is <see cref="FocusOrigin.Ai"/>.</summary>
    public string? OriginalUserRequest { get; set; }

    /// <summary>How many days honoring this focus completes it.</summary>
    public int TotalDays { get; set; }

    /// <summary>Backed by <see cref="FocusCheckIn"/> history, but kept here as a maintained counter for fast reads.</summary>
    public int DaysCompleted { get; set; }

    public int Priority { get; set; }

    public FocusStatus Status { get; set; } = FocusStatus.Active;

    public FocusOrigin Origin { get; set; }

    public DateOnly StartedAt { get; set; }

    public DateOnly? EndedAt { get; set; }

    public List<ComparisonPoint> ComparisonPoints { get; set; } = [];

    public List<FocusCheckIn> CheckIns { get; set; } = [];

    public TrainingUser? TrainingUser { get; set; }

    public IEnumerable<ComparisonPoint> PositivePoints =>
        ComparisonPoints.Where(p => p.Polarity == ComparisonPointPolarity.Positive).OrderBy(p => p.Order);

    public IEnumerable<ComparisonPoint> NegativePoints =>
        ComparisonPoints.Where(p => p.Polarity == ComparisonPointPolarity.Negative).OrderBy(p => p.Order);
}
