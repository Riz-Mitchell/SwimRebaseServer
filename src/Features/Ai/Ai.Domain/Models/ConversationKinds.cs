namespace Ai.Domain.Models;

/// <summary>Values for <see cref="Conversation.Kind"/>. A string (not an enum) so a new sub-agent never needs a domain change - it just names its own kind.</summary>
public static class ConversationKinds
{
    public const string Chat = "chat";
}
