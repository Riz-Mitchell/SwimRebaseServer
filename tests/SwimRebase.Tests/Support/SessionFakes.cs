using Sessions.Application.Interfaces;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests;

/// <summary>Builds the real ingest/service wiring over in-memory storage.</summary>
internal static class TestSessions
{
    public static SessionIngest Ingest() => new(new IntensityResolver([new WordsIntensityMapper()]));

    public static SessionService Service(InMemorySessionRepository repository) => new(repository, Ingest(), new NoOpUnitOfWork());
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
    public List<Session> Sessions { get; } = [];

    public List<SessionFact> Facts { get; } = [];

    public void Add(Session session, IEnumerable<SessionFact> facts)
    {
        Sessions.Add(session);
        Facts.AddRange(facts);
    }

    public Task<Session?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id && s.OwnerId == userId));

    public Task<Session?> FindBySourceDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.SourceDraftId == draftId && s.OwnerId == userId));

    public Task<IReadOnlyList<Session>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(Sessions
            .Where(s => s.OwnerId == userId && (from is null || s.PerformedOn >= from) && (to is null || s.PerformedOn <= to))
            .OrderByDescending(s => s.PerformedOn).ThenByDescending(s => s.CreatedAt).Skip(offset).Take(limit).ToList());

    public Task ReplaceFactsAsync(Guid sessionId, IEnumerable<SessionFact> facts, CancellationToken cancellationToken)
    {
        Facts.RemoveAll(f => f.SessionId == sessionId);
        Facts.AddRange(facts);
        return Task.CompletedTask;
    }
}
