using Sessions.Application.Interfaces;
using Sessions.Application.Load;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Services;

/// <summary>
/// Keeps each session's stored load right and reads the load series. The writes never save: they run inside the caller's unit of work,
/// so a load changes in the same transaction as whatever changed it.
/// </summary>
public sealed class SessionLoadService(
    ISessionRepository sessions,
    ITimedSwimRepository timedSwims,
    ISessionPreferencesRepository preferences,
    ILoadRepository loads)
{
    /// <summary>Sets the load of one session being saved from its leaves, plus any timed swims already counted towards it.</summary>
    public async Task ApplyAsync(Guid userId, Session session, IEnumerable<SessionLeaf> leaves, CancellationToken cancellationToken)
    {
        var defaultLevel = await DefaultEffortAsync(userId, cancellationToken);
        var counted = await timedSwims.ListCountedForSessionsAsync(userId, [session.Id], cancellationToken);
        Set(session, leaves.Select(l => (l.Distance, l.Intensity)).Concat(counted.Select(t => (t.Dist, t.Intensity))), defaultLevel);
    }

    /// <summary>
    /// Rebuilds the load of the given sessions (all the user's when null) from their stored facts and counted timed swims.
    /// <paramref name="defaultLevel"/> is for a default that is changing in this same unit of work and so is not stored yet.
    /// </summary>
    public async Task RecomputeAsync(Guid userId, IReadOnlyCollection<Guid>? sessionIds, int? defaultLevel, CancellationToken cancellationToken)
    {
        var level = defaultLevel ?? await DefaultEffortAsync(userId, cancellationToken);
        var tracked = await sessions.ListTrackedAsync(userId, sessionIds, cancellationToken);
        if (tracked.Count == 0)
        {
            return;
        }

        var ids = tracked.Select(s => s.Id).ToList();
        var facts = (await sessions.ListFactsAsync(ids, cancellationToken)).ToLookup(f => f.SessionId);
        var swims = (await timedSwims.ListCountedForSessionsAsync(userId, ids, cancellationToken)).ToLookup(t => t.SessionId!.Value);

        foreach (var session in tracked)
        {
            Set(
                session,
                facts[session.Id].Select(f => (f.Distance, f.Intensity)).Concat(swims[session.Id].Select(t => (t.Dist, t.Intensity))),
                level);
        }
    }

    /// <exception cref="InvalidLoadQueryException">The query cannot be answered as asked.</exception>
    public async Task<LoadResult> QueryAsync(Guid userId, LoadQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        // Computed once up front so a bad query is rejected before any database work.
        LoadSeriesCalculator.Compute(query, [], today);
        var rows = await loads.ListAsync(userId, LoadSeriesCalculator.FetchFrom(query), query.To, cancellationToken);
        return LoadSeriesCalculator.Compute(query, rows, today);
    }

    public async Task<int> DefaultEffortAsync(Guid userId, CancellationToken cancellationToken) =>
        (await preferences.FindAsync(userId, cancellationToken))?.DefaultEffort ?? SessionPreferences.BuiltInDefaultEffort;

    private static void Set(Session session, IEnumerable<(int Distance, int? Level)> parts, int defaultLevel)
    {
        var load = LoadCalculator.Compute(parts, defaultLevel);
        session.Load = load.Load;
        session.UnratedDistance = load.UnratedDistance;
    }
}
