using Microsoft.EntityFrameworkCore;
using Training.Application.Interfaces;
using Training.Domain.Models;

namespace Training.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="TrainingDbContext"/> - no SaveChanges.</summary>
public sealed class TrainingUserRepository(TrainingDbContext db) : ITrainingUserRepository
{
    public Task<TrainingUser?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.TrainingUsers.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public void Add(TrainingUser user) => db.TrainingUsers.Add(user);

    public Task TouchLastActiveAtAsync(Guid userId, DateTimeOffset lastActiveAt, CancellationToken cancellationToken) =>
        db.TrainingUsers
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastActiveAt, lastActiveAt), cancellationToken);
}
