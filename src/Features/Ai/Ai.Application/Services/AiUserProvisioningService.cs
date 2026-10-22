using Ai.Application.Interfaces;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application;

namespace Ai.Application.Services;

public sealed class AiUserProvisioningService(
    IAiUserRepository aiUsers,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<AiUserProvisioningService>? logger = null) : IAiUserProvisioningService
{
    private readonly ILogger<AiUserProvisioningService> _logger = logger ?? NullLogger<AiUserProvisioningService>.Instance;

    public async Task EnsureExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await aiUsers.FindAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            aiUsers.Add(new AiUser { Id = userId, CreatedAt = now, LastActiveAt = now });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[{ClassName}] Provisioned AI profile for user {UserId}", nameof(AiUserProvisioningService), userId);
            return;
        }

        await aiUsers.TouchLastActiveAtAsync(userId, now, cancellationToken);
    }
}
