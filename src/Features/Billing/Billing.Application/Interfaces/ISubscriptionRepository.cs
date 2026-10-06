using Billing.Domain.Models;

namespace Billing.Application.Interfaces;

/// <summary>Pure query/tracking against the billing store - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ISubscriptionRepository
{
    Task<Subscription?> FindAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Subscription subscription);

    void Remove(Subscription subscription);

    Task<bool> EventSeenAsync(string eventId, CancellationToken cancellationToken);

    void AddEvent(WebhookEvent webhookEvent);
}
