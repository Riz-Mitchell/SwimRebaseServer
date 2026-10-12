using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace SessionConversion.Infrastructure;

public sealed class SessionConversionUserDataEraser(SessionConversionDbContext db) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.SessionDrafts.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}
