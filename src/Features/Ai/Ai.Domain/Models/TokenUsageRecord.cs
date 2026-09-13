namespace Ai.Domain.Models;

/// <summary>One row per model turn (including compaction summarization calls), for per-user cost analysis.</summary>
public class TokenUsageRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ConversationId { get; set; }

    public string Model { get; set; } = string.Empty;

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CacheCreationInputTokens { get; set; }

    public long CacheReadInputTokens { get; set; }

    /// <summary>Distinguishes a normal chat turn from an internal compaction/summarization call.</summary>
    public bool WasCompactionCall { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
