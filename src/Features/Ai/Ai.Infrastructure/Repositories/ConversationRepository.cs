using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Ai.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="AiDbContext"/> - no SaveChanges, no transactions.</summary>
public sealed class ConversationRepository(AiDbContext db) : IConversationRepository
{
    public async Task<Conversation?> FindAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        return conversation is null || conversation.UserId != userId ? null : conversation;
    }

    public async Task<IReadOnlyList<Conversation>> ListForUserAsync(
        Guid userId, ConversationSortBy sortBy, bool descending, CancellationToken cancellationToken)
    {
        var query = db.Conversations.Where(c => c.UserId == userId);

        IOrderedQueryable<Conversation> ordered = (sortBy, descending) switch
        {
            (ConversationSortBy.CreatedAt, true) => query.OrderByDescending(c => c.CreatedAt),
            (ConversationSortBy.CreatedAt, false) => query.OrderBy(c => c.CreatedAt),
            (ConversationSortBy.LastMessageAt, true) => query.OrderByDescending(c => c.LastMessageAt),
            (ConversationSortBy.LastMessageAt, false) => query.OrderBy(c => c.LastMessageAt),
            _ => query.OrderByDescending(c => c.LastMessageAt),
        };

        return await ordered.ToListAsync(cancellationToken);
    }

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public async Task<IReadOnlyList<ConversationMessage>> GetLiveContextMessagesAsync(
        Guid conversationId, CancellationToken cancellationToken) =>
        await db.ConversationMessages
            .Where(m => m.ConversationId == conversationId && !m.IsCompacted)
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesPageAsync(
        Guid conversationId, int? beforeSequenceNumber, int limit, CancellationToken cancellationToken) =>
        await db.ConversationMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Where(m => beforeSequenceNumber == null || m.SequenceNumber < beforeSequenceNumber)
            .OrderByDescending(m => m.SequenceNumber)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

    public async Task<ConversationSummary?> GetLatestSummaryAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.ConversationSummaries
            .Where(s => s.ConversationId == conversationId)
            .OrderByDescending(s => s.CoveredThroughSequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetNextSequenceNumberAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        // Fine for a single writer per conversation (the orchestrator only ever runs one turn at a
        // time for a given conversation). Not safe under concurrent writers - switch to a DB
        // sequence or a serializable transaction if that ever becomes a requirement.
        var maxSequence = await db.ConversationMessages
            .Where(m => m.ConversationId == conversationId)
            .Select(m => (int?)m.SequenceNumber)
            .MaxAsync(cancellationToken);

        return (maxSequence ?? 0) + 1;
    }

    public void AddMessage(ConversationMessage message) => db.ConversationMessages.Add(message);

    public void AddSummary(ConversationSummary summary) => db.ConversationSummaries.Add(summary);

    public Task MarkMessagesCompactedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken) =>
        db.ConversationMessages
            .Where(m => messageIds.Contains(m.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.IsCompacted, true), cancellationToken);

    public Task TouchLastMessageAtAsync(Guid conversationId, DateTimeOffset at, CancellationToken cancellationToken) =>
        db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LastMessageAt, at), cancellationToken);
}
