using Billing.Application.Interfaces;
using Billing.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="BillingDbContext"/> - no SaveChanges.</summary>
public sealed class ConversionUsageRepository(BillingDbContext db) : IConversionUsageRepository
{
    public async Task LockUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = userId.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.ConversionUsages.CountAsync(u => u.UserId == userId && u.CreatedAt >= since && u.RefundedAt == null, cancellationToken);

    public Task<ConversionUsage?> FindByConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        db.ConversionUsages.SingleOrDefaultAsync(u => u.ConversationId == conversationId, cancellationToken);

    public void Add(ConversionUsage usage) => db.ConversionUsages.Add(usage);
}
