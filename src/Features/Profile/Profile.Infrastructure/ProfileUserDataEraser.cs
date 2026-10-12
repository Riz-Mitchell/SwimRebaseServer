using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace Profile.Infrastructure;

public sealed class ProfileUserDataEraser(ProfileDbContext db) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserProfiles.Where(p => p.Id == userId).ExecuteDeleteAsync(cancellationToken);
}
