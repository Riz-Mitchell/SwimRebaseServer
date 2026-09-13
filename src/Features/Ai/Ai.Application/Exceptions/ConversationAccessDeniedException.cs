namespace Ai.Application.Exceptions;

/// <summary>Thrown when a user references a conversation (or tool execution request) they don't own.</summary>
public sealed class ConversationAccessDeniedException(Guid conversationId)
    : Exception($"Conversation '{conversationId}' does not belong to the current user.")
{
    public Guid ConversationId { get; } = conversationId;
}
