using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionsDbContext"/> - no SaveChanges.</summary>
public sealed class SessionRepository(SessionsDbContext db) : ISessionRepository
{
    public void Add(Session session, IEnumerable<SessionFact> facts)
    {
        db.Sessions.Add(session);
        db.SessionFacts.AddRange(facts);
    }

    public async Task<Session?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId, cancellationToken);

    public async Task<Session?> FindBySourceDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken) =>
        await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.SourceDraftId == draftId && s.OwnerId == userId, cancellationToken);

    public async Task<IReadOnlyList<Session>> ListAsync(
        Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken) =>
        await db.Sessions
            .AsNoTracking()
            .Where(s => s.OwnerId == userId
                && (from == null || s.PerformedOn >= from)
                && (to == null || s.PerformedOn <= to))
            .OrderByDescending(s => s.PerformedOn)
            .ThenByDescending(s => s.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task ReplaceFactsAsync(Guid sessionId, IEnumerable<SessionFact> facts, CancellationToken cancellationToken)
    {
        // Tracked delete + insert, so both go out in the same SaveChanges (one transaction) as the session change.
        db.SessionFacts.RemoveRange(await db.SessionFacts.Where(f => f.SessionId == sessionId).ToListAsync(cancellationToken));
        db.SessionFacts.AddRange(facts);
    }
}
