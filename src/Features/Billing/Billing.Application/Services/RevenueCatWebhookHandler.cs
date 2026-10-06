using Billing.Application.Interfaces;
using Billing.Application.Models;
using Billing.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Billing.Application.Services;

/// <summary>
/// Turns RevenueCat events into the stored subscription. RevenueCat retries and can deliver out of order, so an event is applied once
/// (by id) and never over a newer one. Events for users we cannot identify are accepted and dropped so RevenueCat stops retrying.
/// </summary>
public sealed class RevenueCatWebhookHandler(
    ISubscriptionRepository subscriptions,
    [FromKeyedServices(BillingKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker,
    TimeProvider clock)
{
    private static readonly HashSet<string> GrantingTypes =
    [
        "INITIAL_PURCHASE", "RENEWAL", "PRODUCT_CHANGE", "UNCANCELLATION", "NON_RENEWING_PURCHASE", "SUBSCRIPTION_EXTENDED", "CANCELLATION", "EXPIRATION",
    ];

    public async Task<WebhookResult> HandleAsync(RevenueCatEvent e, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(e.Id) || string.IsNullOrEmpty(e.Type))
        {
            return WebhookResult.Ignored;
        }

        if (await subscriptions.EventSeenAsync(e.Id, cancellationToken))
        {
            return WebhookResult.Duplicate;
        }

        var changedUser = e.Type == "TRANSFER" ? await TransferAsync(e, cancellationToken) : await ApplyAsync(e, cancellationToken);
        subscriptions.AddEvent(new WebhookEvent { EventId = e.Id, Type = e.Type, ReceivedAt = clock.GetUtcNow() });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (changedUser is { } userId)
        {
            await eventTracker.TrackAsync(userId, EventTypes.SubscriptionChanged, $"{{\"type\":\"{e.Type}\"}}", cancellationToken);
            return WebhookResult.Applied;
        }

        return WebhookResult.Ignored;
    }

    private async Task<Guid?> ApplyAsync(RevenueCatEvent e, CancellationToken cancellationToken)
    {
        if (!GrantingTypes.Contains(e.Type!) || !Guid.TryParse(e.AppUserId, out var userId) || e.ExpirationAtMs is not { } expirationMs)
        {
            return null;
        }

        var eventAt = e.EventTimestampMs is { } ts ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : clock.GetUtcNow();
        var subscription = await subscriptions.FindAsync(userId, cancellationToken);
        if (subscription is not null && eventAt < subscription.LastEventAt)
        {
            return null;
        }

        if (subscription is null)
        {
            subscription = new Subscription { UserId = userId };
            subscriptions.Add(subscription);
        }

        subscription.ProductId = e.ProductId ?? subscription.ProductId;
        subscription.Store = e.Store ?? subscription.Store;
        subscription.IsSandbox = string.Equals(e.Environment, "SANDBOX", StringComparison.OrdinalIgnoreCase);
        subscription.ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expirationMs);
        subscription.WillRenew = e.Type is "INITIAL_PURCHASE" or "RENEWAL" or "PRODUCT_CHANGE" or "UNCANCELLATION" or "SUBSCRIPTION_EXTENDED";
        subscription.LastEventAt = eventAt;
        subscription.UpdatedAt = clock.GetUtcNow();
        return userId;
    }

    /// <summary>Moves the subscription from the users it was on to the first of the users it moved to.</summary>
    private async Task<Guid?> TransferAsync(RevenueCatEvent e, CancellationToken cancellationToken)
    {
        var target = (e.TransferredTo ?? []).Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null).FirstOrDefault(g => g is not null);
        Subscription? moved = null;
        foreach (var from in (e.TransferredFrom ?? []).Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null).OfType<Guid>())
        {
            var existing = await subscriptions.FindAsync(from, cancellationToken);
            if (existing is null)
            {
                continue;
            }

            moved ??= existing;
            subscriptions.Remove(existing);
        }

        if (moved is null || target is not { } targetId)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var existingTarget = await subscriptions.FindAsync(targetId, cancellationToken);
        if (existingTarget is not null)
        {
            subscriptions.Remove(existingTarget);
        }

        subscriptions.Add(new Subscription
        {
            UserId = targetId,
            ProductId = moved.ProductId,
            Store = moved.Store,
            IsSandbox = moved.IsSandbox,
            ExpiresAt = moved.ExpiresAt,
            WillRenew = moved.WillRenew,
            LastEventAt = e.EventTimestampMs is { } ts ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : now,
            UpdatedAt = now,
        });
        return targetId;
    }
}
