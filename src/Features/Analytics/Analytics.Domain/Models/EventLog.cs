namespace Analytics.Domain.Models;

/// <summary>
/// One recorded app event (e.g. a chat message sent, a focus created). No FK/anchor-row - unlike
/// every other per-user entity in this codebase, nothing else hangs off this table, so UserId is a
/// bare indexed column rather than a constrained relationship to a schema-local anchor.
/// </summary>
public class EventLog
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
