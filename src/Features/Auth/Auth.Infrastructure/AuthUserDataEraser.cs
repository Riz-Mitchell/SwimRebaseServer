using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace Auth.Infrastructure;

public sealed class AuthUserDataEraser(AuthDbContext db) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
}
