using Auth.Application.Interfaces;
using Auth.Domain.Models;

namespace Auth.Application.Services;

public sealed class UserProvisioningService(IUserRepository users, IUnitOfWork unitOfWork) : IUserProvisioningService
{
    public async Task EnsureUserExistsAsync(Guid userId, string? email, CancellationToken cancellationToken)
    {
        var existing = await users.FindAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            users.Add(new AppUser
            {
                Id = userId,
                Email = email,
                CreatedAt = now,
                LastSeenAt = now,
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        await users.TouchLastSeenAtAsync(userId, now, cancellationToken);
    }
}
