using Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Application;

namespace Billing.Infrastructure;

public sealed class BillingUserDataEraser(BillingDbContext db, IRevenueCatSubscriberClient revenueCat) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.ConversionUsages.Where(u => u.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Subscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await revenueCat.DeleteSubscriberAsync(userId, cancellationToken);
    }
}
