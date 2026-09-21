using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>Raises one gated tool call for its <see cref="Kind"/>: tells the client what it is waiting for and sets the persisted request up accordingly.</summary>
public interface IToolInteraction
{
    ToolInteractionKind Kind { get; }

    /// <summary>Called by the turn loop for each gated call, before the request is saved. Returns the events to stream to the client.</summary>
    Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken);
}

public sealed record ToolRaiseContext(
    Guid UserId,
    Guid ConversationId,
    ToolCallRequestedEvent Call,
    ToolExecutionRequest Request,
    IAiToolDefinition? Tool,
    TurnLoopOutcome Outcome);
