using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services;

/// <summary>The main chat's profile (its system prompt extended by each sub-agent's guidance) plus every registered sub-agent's own.</summary>
public sealed class AgentProfileProvider : IAgentProfileProvider
{
    private readonly AgentProfile _chat;
    private readonly Dictionary<string, AgentProfile> _bySubAgentKind;

    public AgentProfileProvider(IEnumerable<ISubAgent> subAgents)
    {
        var agents = subAgents.ToList();
        var addenda = agents.Select(a => a.MainChatPromptAddendum).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

        _chat = AgentProfile.Chat(addenda.Count == 0
            ? AgentProfile.ChatSystemPrompt
            : $"{AgentProfile.ChatSystemPrompt}\n\n{string.Join("\n\n", addenda)}");
        _bySubAgentKind = agents.ToDictionary(a => a.Profile.Kind, a => a.Profile);
    }

    public AgentProfile Get(string conversationKind)
    {
        if (conversationKind == ConversationKinds.Chat)
        {
            return _chat;
        }

        return _bySubAgentKind.TryGetValue(conversationKind, out var profile)
            ? profile
            : throw new InvalidOperationException($"No agent profile is registered for conversation kind '{conversationKind}'.");
    }
}
