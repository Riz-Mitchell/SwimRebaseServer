namespace Ai.Domain.Models;

/// <summary>
/// A rolling summary produced once a conversation crosses the compaction threshold.
/// The latest summary plus any messages after <see cref="CoveredThroughSequenceNumber"/>
/// form the live context sent to the model - keeping token usage bounded regardless of
/// how long the conversation runs.
/// </summary>
public class ConversationSummary
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Messages with SequenceNumber &lt;= this are represented by this summary.</summary>
    public int CoveredThroughSequenceNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Conversation? Conversation { get; set; }
}
