using Profile.Domain.Models;

namespace Profile.Application.Interfaces;

/// <summary>Pure query/tracking against the profile store - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface IUserProfileRepository
{
    /// <summary>The profile with its answers, or null if none exists yet.</summary>
    Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Immediate insert that is a no-op if the profile already exists, so concurrent first calls can't collide.</summary>
    Task EnsureExistsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    void AddAnswer(ProfileAnswer answer);
}
