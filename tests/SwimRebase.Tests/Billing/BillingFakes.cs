using Billing.Application.Interfaces;
using Billing.Application.Options;
using Billing.Application.Services;
using Billing.Domain.Models;
using Microsoft.Extensions.Options;

namespace SwimRebase.Tests.Payments;

internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class InMemorySubscriptions : ISubscriptionRepository
{
    public Dictionary<Guid, Subscription> Subscriptions { get; } = [];

    public HashSet<string> Events { get; } = [];

    public Task<Subscription?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Subscriptions.GetValueOrDefault(userId));

    public void Add(Subscription subscription) => Subscriptions[subscription.UserId] = subscription;

    public void Remove(Subscription subscription) => Subscriptions.Remove(subscription.UserId);

    public Task<bool> EventSeenAsync(string eventId, CancellationToken cancellationToken) => Task.FromResult(Events.Contains(eventId));

    public void AddEvent(WebhookEvent webhookEvent) => Events.Add(webhookEvent.EventId);
}

internal sealed class InMemoryUsage : IConversionUsageRepository
{
    public List<ConversionUsage> Usages { get; } = [];

    public Task LockUserAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult(Usages.Count(u => u.UserId == userId && u.CreatedAt >= since && u.RefundedAt is null));

    public Task<ConversionUsage?> FindByConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(Usages.SingleOrDefault(u => u.ConversationId == conversationId));

    public void Add(ConversionUsage usage) => Usages.Add(usage);
}

internal sealed class FixedTrial(DateTimeOffset? endsAt = null) : ITrialReader
{
    public DateTimeOffset? EndsAt { get; set; } = endsAt;

    public Task<DateTimeOffset?> TrialEndsAtAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(EndsAt);
}

/// <summary>Everything the billing services need, wired to in-memory stores and a clock the test can move.</summary>
internal sealed class BillingHarness
{
    public static readonly Guid UserId = Guid.NewGuid();

    /// <summary>A Wednesday, so the week started on Monday 2026-10-05.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public readonly ManualClock Clock = new(Start);
    public readonly InMemorySubscriptions Subscriptions = new();
    public readonly InMemoryUsage Usage = new();
    public readonly FixedTrial Trial = new();
    public readonly BillingOptions Options = new() { FreeWeeklyConversions = 2, FairUseWeeklyConversions = 50 };
    public readonly EntitlementService Entitlements;
    public readonly ConversionQuotaService Quota;
    public readonly RevenueCatWebhookHandler Webhook;

    public BillingHarness()
    {
        Entitlements = new EntitlementService(Subscriptions, Usage, Trial, Microsoft.Extensions.Options.Options.Create(Options), Clock);
        Quota = new ConversionQuotaService(Entitlements, Usage, new NoOpUnitOfWork(), new NoOpEventTracker(), Clock);
        Webhook = new RevenueCatWebhookHandler(Subscriptions, new NoOpUnitOfWork(), new NoOpEventTracker(), Clock);
    }

    public void GivePaid(DateTimeOffset expiresAt, bool sandbox = false) => Subscriptions.Add(new Subscription
    {
        UserId = UserId, ProductId = "pro_monthly", Store = "APP_STORE", IsSandbox = sandbox, ExpiresAt = expiresAt, WillRenew = true,
        LastEventAt = Start, UpdatedAt = Start,
    });
}
