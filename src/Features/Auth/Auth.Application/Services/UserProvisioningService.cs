using Auth.Application.Interfaces;
using Auth.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Auth.Application.Services;

public sealed class UserProvisioningService(
    IUserRepository users,
    [FromKeyedServices(AuthKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker) : IUserProvisioningService
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
            await eventTracker.TrackAsync(userId, EventTypes.UserSignedUp, null, cancellationToken);
            return;
        }

        await users.TouchLastSeenAtAsync(userId, now, cancellationToken);
    }
}
