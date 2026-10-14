using Microsoft.EntityFrameworkCore;
using Sessions.Application.Interfaces;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="SessionsDbContext"/> - no SaveChanges.</summary>
public sealed class CustomMetricRepository(SessionsDbContext db) : ICustomMetricRepository
{
    public async Task<IReadOnlyList<CustomMetric>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.CustomMetrics.AsNoTracking().Where(m => m.OwnerId == userId).OrderBy(m => m.Position).ToListAsync(cancellationToken);

    public async Task<CustomMetric?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.CustomMetrics.FirstOrDefaultAsync(m => m.OwnerId == userId && m.Id == id, cancellationToken);

    public void Add(CustomMetric metric) => db.CustomMetrics.Add(metric);

    public void Remove(CustomMetric metric) => db.CustomMetrics.Remove(metric);
}
