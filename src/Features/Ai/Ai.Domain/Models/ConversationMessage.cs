namespace Ai.Domain.Models;

/// <summary>
/// One turn in a conversation. <see cref="Content"/> holds plain display text (for UI, search,
/// and summarization); <see cref="RawContentJson"/> holds the exact serialized content blocks
/// (text/tool_use/tool_result) needed to replay accurate history back to the model.
/// </summary>
public class ConversationMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>Monotonic per-conversation order. Drives compaction cutoffs deterministically.</summary>
    public int SequenceNumber { get; set; }

    public MessageRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? RawContentJson { get; set; }

    /// <summary>True once a <see cref="ConversationSummary"/> covers this message; excluded from live context.</summary>
    public bool IsCompacted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Conversation? Conversation { get; set; }
}
