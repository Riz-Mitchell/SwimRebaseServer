using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests;

/// <summary>Builds the real ingest/service wiring over in-memory storage.</summary>
internal static class TestSessions
{
    public static IntensityResolver Resolver() => new([new VocabularyIntensityMapper()]);

    public static SessionIngest Ingest() => new(Resolver());

    public static UserEffortTermService Terms(InMemoryUserEffortTermRepository? repository = null) =>
        new(repository ?? new InMemoryUserEffortTermRepository(), Resolver(), new NoOpUnitOfWork());

    public static IProgressionDefaultsProvider Defaults(ProgressionDefaults? defaults = null) => new FixedProgressionDefaults(defaults ?? ProgressionDefaults.BuiltIn);

    public static SessionService Service(InMemorySessionRepository repository, ProgressionDefaults? defaults = null, InMemorySessionStatsRepository? stats = null) =>
        new(repository, stats ?? new InMemorySessionStatsRepository(), Ingest(), Terms(), Defaults(defaults), new NoOpUnitOfWork());
}

internal sealed class FixedProgressionDefaults(ProgressionDefaults defaults) : IProgressionDefaultsProvider
{
    public Task<ProgressionDefaults> DefaultsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(defaults);
}

internal sealed class InMemoryUserEffortTermRepository : IUserEffortTermRepository
{
    public List<UserEffortTerm> Terms { get; } = [];

    public Task<IReadOnlyList<UserEffortTerm>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserEffortTerm>>(Terms.Where(t => t.OwnerId == userId).ToList());

    public Task<UserEffortTerm?> FindAsync(Guid userId, string phrase, CancellationToken cancellationToken) =>
        Task.FromResult(Terms.FirstOrDefault(t => t.OwnerId == userId && t.Phrase == phrase));

    public void Add(UserEffortTerm term) => Terms.Add(term);
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

    public void Remove(Session session)
    {
        Sessions.Remove(session);
        Facts.RemoveAll(f => f.SessionId == session.Id);
    }

    public Task<Session?> FindBySourceDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.SourceDraftId == draftId && s.OwnerId == userId));

    public Task<IReadOnlyList<Session>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(Sessions
            .Where(s => s.OwnerId == userId && (from is null || s.PerformedOn >= from) && (to is null || s.PerformedOn <= to))
            .OrderByDescending(s => s.PerformedOn).ThenByDescending(s => s.CreatedAt).Skip(offset).Take(limit).ToList());

    public Task<IReadOnlyList<string>> RecentTitlesAsync(Guid userId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Sessions
            .Where(s => s.OwnerId == userId)
            .OrderByDescending(s => s.PerformedOn).ThenByDescending(s => s.CreatedAt).Take(take).Select(s => s.Title).ToList());

    public Task ReplaceFactsAsync(Guid sessionId, IEnumerable<SessionFact> facts, CancellationToken cancellationToken)
    {
        Facts.RemoveAll(f => f.SessionId == sessionId);
        Facts.AddRange(facts);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySessionStatsRepository : ISessionStatsRepository
{
    private readonly Dictionary<Guid, int> _counts = [];

    /// <summary>How many times the count was adjusted, to show a save that returned early did not touch it.</summary>
    public int Adjustments { get; private set; }

    public Task AdjustAsync(Guid userId, int delta, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Adjustments++;
        _counts[userId] = Math.Max(_counts.GetValueOrDefault(userId) + delta, 0);
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(_counts.GetValueOrDefault(userId));
}

internal sealed class InMemoryPreferencesRepository : ISessionPreferencesRepository
{
    public List<SessionPreferences> Rows { get; } = [];

    public Task<SessionPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.OwnerId == userId));

    public void Add(SessionPreferences preferences) => Rows.Add(preferences);
}

internal sealed class InMemoryCustomMetricRepository : ICustomMetricRepository
{
    private readonly List<CustomMetric> rows = [];

    public Task<IReadOnlyList<CustomMetric>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomMetric>>(rows.Where(m => m.OwnerId == userId).OrderBy(m => m.Position).ToList());

    public Task<CustomMetric?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(rows.FirstOrDefault(m => m.OwnerId == userId && m.Id == id));

    public void Add(CustomMetric metric) => rows.Add(metric);

    public void Remove(CustomMetric metric) => rows.Remove(metric);
}

/// <summary>Hands the calculator every fact it was given; the real repository filters in SQL and the calculator applies the same filters.</summary>
internal sealed class InMemoryMetricsRepository(IReadOnlyList<FactTotal> facts, DateOnly? firstPerformedOn = null) : IMetricsRepository
{
    public Task<IReadOnlyList<FactTotal>> GetTotalsAsync(Guid userId, MetricsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(facts);

    public Task<DateOnly?> FirstPerformedOnAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(firstPerformedOn);
}
