using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace Sessions.Infrastructure;

public sealed class SessionsUserDataEraser(SessionsDbContext db) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.TimedSwims.Where(t => t.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Sessions.Where(s => s.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.SessionPreferences.Where(p => p.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserEffortTerms.Where(t => t.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.CustomMetrics.Where(m => m.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
