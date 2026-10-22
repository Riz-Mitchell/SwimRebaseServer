using Billing.Application.Interfaces;
using Billing.Application.Models;
using Billing.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application;

namespace Billing.Application.Services;

public sealed class ConversionQuotaService(
    EntitlementService entitlements,
    IConversionUsageRepository usage,
    [FromKeyedServices(BillingKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker,
    TimeProvider clock,
    ILogger<ConversionQuotaService>? logger = null) : IConversionQuota
{
    private readonly ILogger<ConversionQuotaService> _logger = logger ?? NullLogger<ConversionQuotaService>.Instance;

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
            _logger.LogWarning("[{ClassName}] Conversion quota exhausted for user {UserId} on plan {Plan}: {Used}/{Limit}",
                nameof(ConversionQuotaService), userId, plan, used, quota.Limit);
            await eventTracker.TrackAsync(userId, EventTypes.ConversionQuotaExceeded, $"{{\"plan\":\"{plan}\"}}", cancellationToken);
            return new QuotaDecision(false, quota);
        }

        usage.Add(new ConversionUsage { Id = Guid.NewGuid(), UserId = userId, ConversationId = conversationId, CreatedAt = now });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Consumed conversion {Used}/{Limit} for user {UserId} on conversation {ConversationId}",
            nameof(ConversionQuotaService), used + 1, quota.Limit, userId, conversationId);
        return new QuotaDecision(true, quota with { Used = used + 1 });
    }

    public async Task RefundAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var entry = await usage.FindByConversationAsync(conversationId, cancellationToken);
        if (entry is null || entry.RefundedAt is not null)
        {
            _logger.LogInformation("[{ClassName}] No refundable conversion for conversation {ConversationId} (missing or already refunded)",
                nameof(ConversionQuotaService), conversationId);
            return;
        }

        entry.RefundedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Refunded conversion for conversation {ConversationId}", nameof(ConversionQuotaService), conversationId);
    }
}
