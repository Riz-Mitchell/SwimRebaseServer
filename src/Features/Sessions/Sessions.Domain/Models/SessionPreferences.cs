namespace Sessions.Domain.Models;

/// <summary>A swimmer's own settings for how sessions are measured. One row per user, created the first time they change one.</summary>
public class SessionPreferences
{
    /// <summary>The default effort, in percent, when a swimmer has not chosen one.</summary>
    public const int BuiltInDefaultEffort = 50;

    public Guid OwnerId { get; set; }

    /// <summary>The intensity percent (1-100) counted for distance whose effort could not be mapped.</summary>
    public int DefaultEffort { get; set; } = BuiltInDefaultEffort;

    /// <summary>The scale intensity is grouped by when a metrics request does not name one: a built-in scale id, or "custom".</summary>
    public string EffortScale { get; set; } = Calculations.EffortScale.TensId;

    /// <summary>The swimmer's own bands as JSON (a list of <see cref="Calculations.EffortBand"/>); null until they save one.</summary>
    public string? CustomEffortScaleJson { get; set; }
}
