using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface IToolExecutionRepository
{
    void Add(ToolExecutionRequest request);

    /// <summary>Scoped to <paramref name="userId"/> - returns null if the request belongs to someone else.</summary>
    Task<ToolExecutionRequest?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Every call recorded for one assistant turn - the gated ones, plus any plain calls run ahead of them - used to detect when the whole turn is resolved.</summary>
    Task<IReadOnlyList<ToolExecutionRequest>> GetByAssistantMessageIdAsync(Guid assistantMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// Still-open requests (<see cref="ToolExecutionStatus.PendingApproval"/>, or <see cref="ToolExecutionStatus.InProgress"/> for a running sub-agent) for one conversation. Lets a client that
    /// missed the original SSE event (e.g. reconnecting after the server crashed mid-turn) discover what it still
    /// needs to resolve, instead of the conversation appearing stuck with no way to find the outstanding request.
    /// Includes requests in the conversations of any sub-agents it started, since those are what the user has to answer.
    /// </summary>
    Task<IReadOnlyList<ToolExecutionRequest>> GetPendingForConversationAsync(
        Guid userId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>True if the conversation has a call still waiting on a human (pending approval/answer) or on a running sub-agent - anything that leaves a tool_use without its tool_result.</summary>
    Task<bool> HasOutstandingRequestAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>How many calls of <paramref name="toolName"/> the conversation has made so far, including any still open.</summary>
    Task<int> CountForConversationAsync(Guid conversationId, string toolName, CancellationToken cancellationToken);

    /// <summary>Every call of <paramref name="toolName"/> in the conversation, oldest first.</summary>
    Task<IReadOnlyList<ToolExecutionRequest>> ListForConversationAsync(Guid conversationId, string toolName, CancellationToken cancellationToken);

    /// <summary>Immediate bulk update (bypasses the change tracker): denies every still-pending call in the conversation, e.g. when its sub-agent is cancelled.</summary>
    Task DenyOutstandingForConversationAsync(Guid conversationId, string reason, CancellationToken cancellationToken);

    /// <summary>Immediate bulk update (bypasses the change tracker): denies every still-pending call across all given conversations, e.g. when a conversation tree is archived.</summary>
    Task DenyOutstandingForConversationsAsync(IReadOnlyList<Guid> conversationIds, string reason, CancellationToken cancellationToken);
}
