using Training.Domain.Models;

namespace Training.Application.Interfaces;

/// <summary>Pure query/tracking against the Training schema's own per-user anchor row - no SaveChanges.</summary>
public interface ITrainingUserRepository
{
    Task<TrainingUser?> FindAsync(Guid userId, CancellationToken cancellationToken);

    void Add(TrainingUser user);

    /// <summary>Immediate bulk update, bypassing the change tracker.</summary>
    Task TouchLastActiveAtAsync(Guid userId, DateTimeOffset lastActiveAt, CancellationToken cancellationToken);
}
