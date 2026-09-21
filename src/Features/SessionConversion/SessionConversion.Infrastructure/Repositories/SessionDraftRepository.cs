using Microsoft.EntityFrameworkCore;
using SessionConversion.Application.Interfaces;
using SessionConversion.Domain.Models;

namespace SessionConversion.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionConversionDbContext"/> - no SaveChanges.</summary>
public sealed class SessionDraftRepository(SessionConversionDbContext db) : ISessionDraftRepository
{
    public void Add(SessionDraft draft) => db.SessionDrafts.Add(draft);

    public async Task<SessionDraft?> FindAsync(Guid userId, Guid draftId, CancellationToken cancellationToken) =>
        await db.SessionDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.UserId == userId, cancellationToken);

    public async Task<SessionDraft?> FindLatestForConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await db.SessionDrafts
            .Where(d => d.UserId == userId && d.SourceConversationId == conversationId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
