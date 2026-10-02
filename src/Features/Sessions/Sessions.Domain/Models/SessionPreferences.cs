namespace Sessions.Domain.Models;

/// <summary>A swimmer's own settings for how sessions are measured. One row per user, created the first time they change one.</summary>
public class SessionPreferences
{
    /// <summary>The default effort level when a swimmer has not chosen one.</summary>
    public const int BuiltInDefaultEffort = 5;

    public Guid OwnerId { get; set; }

    /// <summary>The 1-10 intensity level counted for distance whose effort could not be mapped.</summary>
    public int DefaultEffort { get; set; } = BuiltInDefaultEffort;
}
