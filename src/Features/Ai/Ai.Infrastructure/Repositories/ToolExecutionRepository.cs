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
                && t.ConversationId == conversationId
                && t.Status == ToolExecutionStatus.PendingApproval)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
}
