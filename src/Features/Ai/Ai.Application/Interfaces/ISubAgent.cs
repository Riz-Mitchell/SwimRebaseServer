using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>
/// A specialist that runs in its own conversation, started by one tool call in the main chat and reported back as that
/// call's tool_result. The main chat stays blocked (the tool call is <see cref="ToolExecutionStatus.InProgress"/>) while
/// it runs; anything it asks the user streams to the same client as the main chat's own events.
/// </summary>
public interface ISubAgent
{
    /// <summary>The main-chat tool (an <see cref="IAiToolDefinition"/> with <see cref="ToolInteractionKind.SubAgent"/>) that starts this sub-agent.</summary>
    string ToolName { get; }

    AgentProfile Profile { get; }

    string ConversationTitle { get; }

    /// <summary>
    /// The first user message of the child conversation, built from the input the main chat's model gave the launching tool.
    /// Any images from the parent's latest image message are attached to it.
    /// </summary>
    string BuildOpeningMessage(string toolInputJson);

    /// <summary>Extra guidance for the main chat's system prompt, so the outer model knows when to call <see cref="ToolName"/> and what to do with the result.</summary>
    string? MainChatPromptAddendum { get; }

    /// <summary>Builds the parent tool call's result from the finished child conversation.</summary>
    Task<string> SummarizeAsync(SubAgentCompletion completion, CancellationToken cancellationToken);

    /// <summary>
    /// A gated call to put straight to the user once the sub-agent has finished, instead of resuming the main chat's model first (a save the
    /// swimmer only has to accept). Null resumes the model as usual. Only asked of a sub-agent that succeeded.
    /// </summary>
    Task<ToolCallRequestedEvent?> FollowUpAsync(SubAgentCompletion completion, CancellationToken cancellationToken) =>
        Task.FromResult<ToolCallRequestedEvent?>(null);
}

public sealed record SubAgentCompletion(Guid UserId, Guid ChildConversationId, ToolExecutionRequest ParentRequest);
