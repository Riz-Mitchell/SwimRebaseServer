using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace Analytics.Infrastructure;

public sealed class AnalyticsUserDataEraser(AnalyticsDbContext db) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.EventLogs.Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}
