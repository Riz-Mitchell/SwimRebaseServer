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

    /// <summary>Denormalized for scoping queries directly by user without a join.</summary>
    public Guid UserId { get; set; }

    /// <summary>The originating assistant message this tool_use block belongs to.</summary>
    public Guid AssistantMessageId { get; set; }

    /// <summary>The model's tool_use block id - required to correlate the eventual tool_result.</summary>
    public string AnthropicToolUseId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string InputJson { get; set; } = string.Empty;

    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.PendingApproval;

    public string? ResultJson { get; set; }

    public string? DenialReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Conversation? Conversation { get; set; }
}
