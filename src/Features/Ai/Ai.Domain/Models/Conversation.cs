namespace Ai.Domain.Models;

public class Conversation
{
    public Guid Id { get; set; }

    /// <summary>Supabase auth user id (JWT `sub` claim) - also the FK to <see cref="AiUser"/>. All access must be scoped to this.</summary>
    public Guid UserId { get; set; }

    public string? Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastMessageAt { get; set; }

    public AiUser? AiUser { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];

    public List<ConversationSummary> Summaries { get; set; } = [];
}
