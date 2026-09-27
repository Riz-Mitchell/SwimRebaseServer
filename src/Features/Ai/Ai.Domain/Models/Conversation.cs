namespace Ai.Domain.Models;

public class Conversation
{
    public Guid Id { get; set; }

    /// <summary>Supabase auth user id (JWT `sub` claim) - also the FK to <see cref="AiUser"/>. All access must be scoped to this.</summary>
    public Guid UserId { get; set; }

    public string? Title { get; set; }

    /// <summary>See <see cref="ConversationKinds"/>. Anything other than <see cref="ConversationKinds.Chat"/> is a sub-agent's private conversation, started by a tool call in another one.</summary>
    public string Kind { get; set; } = ConversationKinds.Chat;

    /// <summary>For a sub-agent conversation: the <see cref="ToolExecutionRequest"/> (in the parent chat) whose tool_result this conversation will eventually produce. Null for a normal chat.</summary>
    public Guid? ParentToolExecutionRequestId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastMessageAt { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public AiUser? AiUser { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];

    public List<ConversationSummary> Summaries { get; set; } = [];
}
