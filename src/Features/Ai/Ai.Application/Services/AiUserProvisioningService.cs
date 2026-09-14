using Ai.Application.Interfaces;
using Ai.Domain.Models;

namespace Ai.Application.Services;

public sealed class AiUserProvisioningService(IAiUserRepository aiUsers, IUnitOfWork unitOfWork) : IAiUserProvisioningService
{
    public async Task EnsureExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await aiUsers.FindAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            aiUsers.Add(new AiUser { Id = userId, CreatedAt = now, LastActiveAt = now });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        await aiUsers.TouchLastActiveAtAsync(userId, now, cancellationToken);
    }
}
