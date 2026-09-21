namespace Ai.Domain.Models;

/// <summary>
/// A guarded (write/destructive) tool call the model requested. Created in
/// <see cref="ToolExecutionStatus.PendingApproval"/> and never auto-executed - the conversation
/// turn pauses until a human resolves it via approve/deny. Read-only tools never create a row here.
/// </summary>
public class ToolExecutionRequest
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>FK to <see cref="AiUser"/> - carried directly here (not just via the conversation) so the guard's core query can scope by user without a join.</summary>
    public Guid UserId { get; set; }

    /// <summary>The originating assistant message this tool_use block belongs to.</summary>
    public Guid AssistantMessageId { get; set; }

    /// <summary>The model's tool_use block id - required to correlate the eventual tool_result.</summary>
    public string AnthropicToolUseId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string InputJson { get; set; } = string.Empty;

    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.PendingApproval;

    public string? ResultJson { get; set; }

    /// <summary>What the tool reported it affected. Only recorded for calls executed ahead of their turn's gated calls (see <c>ChatTurnLoop</c>), so the combined tool_result can still report it.</summary>
    public int? AffectedCount { get; set; }

    public string? DenialReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public AiUser? AiUser { get; set; }

    public Conversation? Conversation { get; set; }
}
