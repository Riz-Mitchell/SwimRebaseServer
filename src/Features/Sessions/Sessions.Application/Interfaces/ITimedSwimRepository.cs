using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace Sessions.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ITimedSwimRepository
{
    void Add(TimedSwim timedSwim);

    /// <summary>Scoped to <paramref name="userId"/> - returns null if the swim belongs to someone else. Read-only, with resistances.</summary>
    Task<TimedSwim?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Like <see cref="FindAsync"/> but tracked, so changes to it are saved with the unit of work.</summary>
    Task<TimedSwim?> FindForUpdateAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Swaps a tracked swim's resistances for <paramref name="replacements"/>.</summary>
    void ReplaceResistances(TimedSwim timedSwim, IReadOnlyList<TimedSwimResistance> replacements);

    void Remove(TimedSwim timedSwim);

    /// <summary>Fastest first.</summary>
    Task<IReadOnlyList<TimedSwim>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken);

    /// <summary>The swims matching the query's filters, narrowed to what metrics group by.</summary>
    Task<IReadOnlyList<TimedSwimFact>> GetFactsAsync(Guid userId, TimedSwimMetricsQuery query, CancellationToken cancellationToken);
}
