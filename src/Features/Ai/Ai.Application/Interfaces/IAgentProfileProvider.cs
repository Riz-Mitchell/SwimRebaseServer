using Ai.Application.Models;

namespace Ai.Application.Interfaces;

/// <summary>Finds the <see cref="AgentProfile"/> for a <see cref="Ai.Domain.Models.Conversation.Kind"/>.</summary>
public interface IAgentProfileProvider
{
    AgentProfile Get(string conversationKind);
}
