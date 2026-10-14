using Sessions.Domain.Models;

namespace Sessions.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ICustomMetricRepository
{
    /// <summary>In home-page order.</summary>
    Task<IReadOnlyList<CustomMetric>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked, so it can be edited.</summary>
    Task<CustomMetric?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    void Add(CustomMetric metric);

    void Remove(CustomMetric metric);
}
