using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>Pure query/tracking against the Ai schema's own per-user anchor row - no SaveChanges.</summary>
public interface IAiUserRepository
{
    Task<AiUser?> FindAsync(Guid userId, CancellationToken cancellationToken);

    void Add(AiUser user);

    /// <summary>Immediate bulk update, bypassing the change tracker.</summary>
    Task TouchLastActiveAtAsync(Guid userId, DateTimeOffset lastActiveAt, CancellationToken cancellationToken);
}
