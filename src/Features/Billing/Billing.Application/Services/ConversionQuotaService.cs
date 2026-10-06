using Billing.Application.Interfaces;
using Billing.Application.Models;
using Billing.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Billing.Application.Services;

public sealed class ConversionQuotaService(
    EntitlementService entitlements,
    IConversionUsageRepository usage,
    [FromKeyedServices(BillingKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker,
    TimeProvider clock) : IConversionQuota
{
    public async Task<QuotaDecision> TryConsumeAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await usage.LockUserAsync(userId, cancellationToken);

        var (plan, _, _) = await entitlements.ResolveAsync(userId, cancellationToken);
        var used = await usage.CountSinceAsync(userId, EntitlementService.WeekStart(now), cancellationToken);
        var quota = entitlements.Snapshot(plan, used);
        if (used >= quota.Limit)
        {
            await eventTracker.TrackAsync(userId, EventTypes.ConversionQuotaExceeded, $"{{\"plan\":\"{plan}\"}}", cancellationToken);
            return new QuotaDecision(false, quota);
        }

        usage.Add(new ConversionUsage { Id = Guid.NewGuid(), UserId = userId, ConversationId = conversationId, CreatedAt = now });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new QuotaDecision(true, quota with { Used = used + 1 });
    }

    public async Task RefundAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var entry = await usage.FindByConversationAsync(conversationId, cancellationToken);
        if (entry is null || entry.RefundedAt is not null)
        {
            return;
        }

        entry.RefundedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
