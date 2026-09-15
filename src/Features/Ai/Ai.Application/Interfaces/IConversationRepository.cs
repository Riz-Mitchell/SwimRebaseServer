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

    Task<IReadOnlyList<Conversation>> ListForUserAsync(
        Guid userId, ConversationSortBy sortBy, bool descending, CancellationToken cancellationToken);

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
}
