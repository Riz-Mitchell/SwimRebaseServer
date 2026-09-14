using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;
using Training.Application.Interfaces;
using Training.Domain.Models;

namespace Training.Application.Services;

public sealed class TrainingUserProvisioningService(
    ITrainingUserRepository trainingUsers,
    [FromKeyedServices(TrainingKeys.UnitOfWork)] IUnitOfWork unitOfWork) : ITrainingUserProvisioningService
{
    public async Task EnsureExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await trainingUsers.FindAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            trainingUsers.Add(new TrainingUser { Id = userId, CreatedAt = now, LastActiveAt = now });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        await trainingUsers.TouchLastActiveAtAsync(userId, now, cancellationToken);
    }
}
