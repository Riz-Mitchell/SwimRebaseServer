using Ai.Application.Interfaces;
using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Ai.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="AiDbContext"/> - no SaveChanges.</summary>
public sealed class ToolExecutionRepository(AiDbContext db) : IToolExecutionRepository
{
    public void Add(ToolExecutionRequest request) => db.ToolExecutionRequests.Add(request);

    public async Task<ToolExecutionRequest?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.ToolExecutionRequests.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ToolExecutionRequest>> GetByAssistantMessageIdAsync(
        Guid assistantMessageId, CancellationToken cancellationToken) =>
        await db.ToolExecutionRequests
            .Where(t => t.AssistantMessageId == assistantMessageId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ToolExecutionRequest>> GetPendingForConversationAsync(
        Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await db.ToolExecutionRequests
            .AsNoTracking()
            .Where(t => t.UserId == userId
                && (t.Status == ToolExecutionStatus.PendingApproval || t.Status == ToolExecutionStatus.InProgress)
                && (t.ConversationId == conversationId
                    || db.Conversations.Any(child => child.Id == t.ConversationId
                        && db.ToolExecutionRequests.Any(parent => parent.Id == child.ParentToolExecutionRequestId && parent.ConversationId == conversationId))))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasOutstandingRequestAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await db.ToolExecutionRequests.AnyAsync(
            t => t.UserId == userId
                && t.ConversationId == conversationId
                && (t.Status == ToolExecutionStatus.PendingApproval || t.Status == ToolExecutionStatus.InProgress),
            cancellationToken);

    public async Task<int> CountForConversationAsync(Guid conversationId, string toolName, CancellationToken cancellationToken) =>
        await db.ToolExecutionRequests.CountAsync(t => t.ConversationId == conversationId && t.ToolName == toolName, cancellationToken);

    public async Task<IReadOnlyList<ToolExecutionRequest>> ListForConversationAsync(Guid conversationId, string toolName, CancellationToken cancellationToken) =>
        await db.ToolExecutionRequests
            .AsNoTracking()
            .Where(t => t.ConversationId == conversationId && t.ToolName == toolName)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task DenyOutstandingForConversationAsync(Guid conversationId, string reason, CancellationToken cancellationToken) =>
        db.ToolExecutionRequests
            .Where(t => t.ConversationId == conversationId && t.Status == ToolExecutionStatus.PendingApproval)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, ToolExecutionStatus.Denied)
                .SetProperty(t => t.DenialReason, reason)
                .SetProperty(t => t.ResolvedAt, DateTimeOffset.UtcNow), cancellationToken);
}
