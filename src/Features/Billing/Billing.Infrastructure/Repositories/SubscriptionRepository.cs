using Billing.Application.Interfaces;
using Billing.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="BillingDbContext"/> - no SaveChanges.</summary>
public sealed class SubscriptionRepository(BillingDbContext db) : ISubscriptionRepository
{
    public Task<Subscription?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Subscriptions.SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public void Add(Subscription subscription) => db.Subscriptions.Add(subscription);

    public void Remove(Subscription subscription) => db.Subscriptions.Remove(subscription);

    public Task<bool> EventSeenAsync(string eventId, CancellationToken cancellationToken) =>
        db.WebhookEvents.AnyAsync(e => e.EventId == eventId, cancellationToken);

    public void AddEvent(WebhookEvent webhookEvent) => db.WebhookEvents.Add(webhookEvent);
}
