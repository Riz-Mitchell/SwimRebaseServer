using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>
/// Pure query/tracking - no SaveChanges, no transactions (see <see cref="Shared.Application.IUnitOfWork"/>). Every
/// read is scoped to a caller-supplied user id, never re-derived from the entity itself.
/// </summary>
public interface IConversationRepository
{
    Task<Conversation?> FindAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>The sub-agent conversation started by the given tool call in a parent chat, if any.</summary>
    Task<Conversation?> FindChildAsync(Guid userId, Guid parentToolExecutionRequestId, CancellationToken cancellationToken);

    /// <summary>Only the user's own chats - sub-agent conversations are an implementation detail of the chat that started them.</summary>
    Task<IReadOnlyList<Conversation>> ListForUserAsync(
        Guid userId, ConversationSortBy sortBy, bool descending, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>
    /// The conversation's own id plus every sub-agent conversation nested under it, however deep - found by
    /// walking <see cref="Conversation.ParentToolExecutionRequestId"/> outward through each level's
    /// <see cref="ToolExecutionRequest"/> ids (that link has no FK, so it can't be a normal EF include).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetConversationTreeIdsAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(Conversation conversation);

    /// <summary>Messages not yet folded into a summary, in order - what actually gets sent to the model.</summary>
    Task<IReadOnlyList<ConversationMessage>> GetLiveContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Newest-first keyset page of a conversation's messages. Pass the smallest <see cref="ConversationMessage.SequenceNumber"/>
    /// already seen as <paramref name="beforeSequenceNumber"/> to fetch the next (older) page; omit it for the first page.
    /// Requests <paramref name="limit"/> + 1 rows internally so the caller can tell whether more pages remain.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> GetMessagesPageAsync(
        Guid conversationId, int? beforeSequenceNumber, int limit, CancellationToken cancellationToken);

    Task<ConversationSummary?> GetLatestSummaryAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<int> GetNextSequenceNumberAsync(Guid conversationId, CancellationToken cancellationToken);

    void AddMessage(ConversationMessage message);

    void AddSummary(ConversationSummary summary);

    /// <summary>Immediate bulk update (bypasses the change tracker) - safe to call inside an open <see cref="Shared.Application.IUnitOfWorkTransaction"/>.</summary>
    Task MarkMessagesCompactedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken);

    /// <summary>Immediate bulk update (bypasses the change tracker).</summary>
    Task TouchLastMessageAtAsync(Guid conversationId, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Immediate bulk update (bypasses the change tracker): archives every conversation in <paramref name="conversationIds"/>.</summary>
    Task ArchiveAsync(IReadOnlyList<Guid> conversationIds, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Immediate bulk update (bypasses the change tracker). Only the given conversation - unarchiving does not cascade to its sub-agent tree.</summary>
    Task UnarchiveAsync(Guid conversationId, CancellationToken cancellationToken);
}
