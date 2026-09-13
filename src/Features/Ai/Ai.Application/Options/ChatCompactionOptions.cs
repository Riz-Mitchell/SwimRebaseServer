namespace Ai.Application.Options;

public sealed class ChatCompactionOptions
{
    public const string SectionName = "Ai:Compaction";

    /// <summary>Once live (non-compacted) messages exceed this count, the oldest are folded into a summary.</summary>
    public int MaxLiveMessagesBeforeCompaction { get; set; } = 24;

    /// <summary>Most-recent messages always kept verbatim, even after compaction runs.</summary>
    public int MessagesToRetainAfterCompaction { get; set; } = 8;

    /// <summary>Cheap model used for the internal summarization call - never the user-facing chat model.</summary>
    public string SummarizationModel { get; set; } = "claude-haiku-4-5";

    public int SummarizationMaxTokens { get; set; } = 1024;
}
