using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace Training.Infrastructure;

public sealed class TrainingUserDataEraser(TrainingDbContext db) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.TrainingUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
}
