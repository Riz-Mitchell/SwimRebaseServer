namespace Sessions.Domain.Models;

/// <summary>
/// A swimmer's running totals, so they are read from one row instead of counted over every session. One row per user, created
/// when their first session is saved; a user with no row has no sessions.
/// </summary>
public class UserSessionStats
{
    public Guid OwnerId { get; set; }

    /// <summary>How many sessions the swimmer has saved, kept in step with every create and delete.</summary>
    public int SessionCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
