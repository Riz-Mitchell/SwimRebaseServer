using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace Sessions.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ITimedSwimRepository
{
    void Add(TimedSwim timedSwim);

    /// <summary>Scoped to <paramref name="userId"/> - returns null if the swim belongs to someone else.</summary>
    Task<TimedSwim?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Fastest first.</summary>
    Task<IReadOnlyList<TimedSwim>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken);

    /// <summary>The swims that count towards the given sessions' volume: opted in to metrics and logged against one of them.</summary>
    Task<IReadOnlyList<TimedSwim>> ListCountedForSessionsAsync(Guid userId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);
}
