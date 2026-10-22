using Auth.Application.Interfaces;
using Auth.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application;

namespace Auth.Application.Services;

public sealed class UserProvisioningService(
    IUserRepository users,
    [FromKeyedServices(AuthKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker,
    ILogger<UserProvisioningService>? logger = null) : IUserProvisioningService
{
    private readonly ILogger<UserProvisioningService> _logger = logger ?? NullLogger<UserProvisioningService>.Instance;

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
            _logger.LogInformation("[{ClassName}] Provisioned new user {UserId}", nameof(UserProvisioningService), userId);
            await eventTracker.TrackAsync(userId, EventTypes.UserSignedUp, null, cancellationToken);
            return;
        }

        await users.TouchLastSeenAtAsync(userId, now, cancellationToken);
    }
}
