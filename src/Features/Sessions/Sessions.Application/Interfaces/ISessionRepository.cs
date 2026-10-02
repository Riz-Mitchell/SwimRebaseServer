using Sessions.Domain.Models;

namespace Sessions.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ISessionRepository
{
    void Add(Session session, IEnumerable<SessionFact> facts);

    /// <summary>Scoped to <paramref name="userId"/> - returns null if the session belongs to someone else. Tracked, so it can be edited.</summary>
    Task<Session?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    Task<Session?> FindBySourceDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken);

    /// <summary>Newest first (by date swum, then by when it was saved).</summary>
    Task<IReadOnlyList<Session>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken);

    /// <summary>Swaps a session's fact rows for <paramref name="facts"/> in the same unit of work as the session change.</summary>
    Task ReplaceFactsAsync(Guid sessionId, IEnumerable<SessionFact> facts, CancellationToken cancellationToken);

    /// <summary>Tracked, so their load can be rewritten. All of the user's sessions when <paramref name="ids"/> is null.</summary>
    Task<IReadOnlyList<Session>> ListTrackedAsync(Guid userId, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionFact>> ListFactsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);
}
