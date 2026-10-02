using Sessions.Application.Interfaces;
using Sessions.Application.Load;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests;

/// <summary>Builds the real ingest/service wiring over in-memory storage.</summary>
internal static class TestSessions
{
    public static SessionIngest Ingest() => new(new IntensityResolver([new WordsIntensityMapper()]));

    public static SessionLoadService Load(InMemorySessionRepository sessions, InMemoryTimedSwimRepository? timedSwims = null, InMemoryPreferencesRepository? preferences = null) =>
        new(sessions, timedSwims ?? new InMemoryTimedSwimRepository(), preferences ?? new InMemoryPreferencesRepository(), sessions);

    public static SessionService Service(InMemorySessionRepository repository, SessionLoadService? load = null) =>
        new(repository, Ingest(), load ?? Load(repository), new NoOpUnitOfWork());
}

internal sealed class InMemorySessionRepository : ISessionRepository, ILoadRepository
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

    public Task<IReadOnlyList<Session>> ListTrackedAsync(Guid userId, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(Sessions.Where(s => s.OwnerId == userId && (ids is null || ids.Contains(s.Id))).ToList());

    public Task<IReadOnlyList<SessionFact>> ListFactsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SessionFact>>(Facts.Where(f => sessionIds.Contains(f.SessionId)).ToList());

    public Task<IReadOnlyList<SessionLoadRow>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SessionLoadRow>>(Sessions
            .Where(s => s.OwnerId == userId && (from is null || s.PerformedOn >= from) && (to is null || s.PerformedOn <= to))
            .Select(s => new SessionLoadRow(s.Id, s.PerformedOn, s.Load, s.UnratedDistance, s.Unit))
            .ToList());
}

internal sealed class InMemoryTimedSwimRepository : ITimedSwimRepository
{
    public List<TimedSwim> Swims { get; } = [];

    public void Add(TimedSwim timedSwim) => Swims.Add(timedSwim);

    public Task<TimedSwim?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Swims.FirstOrDefault(t => t.Id == id && t.OwnerId == userId));

    public Task<IReadOnlyList<TimedSwim>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TimedSwim>> ListCountedForSessionsAsync(Guid userId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TimedSwim>>(Swims
            .Where(t => t.OwnerId == userId && t.IncludeInMetrics && t.SessionId is { } id && sessionIds.Contains(id))
            .ToList());
}

internal sealed class InMemoryPreferencesRepository : ISessionPreferencesRepository
{
    public List<SessionPreferences> Rows { get; } = [];

    public Task<SessionPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.OwnerId == userId));

    public void Add(SessionPreferences preferences) => Rows.Add(preferences);
}
