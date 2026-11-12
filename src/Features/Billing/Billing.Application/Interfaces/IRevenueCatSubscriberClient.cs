namespace Billing.Application.Interfaces;

public interface IRevenueCatSubscriberClient
{
    /// <summary>Throws <see cref="InvalidOperationException"/> if the configuration needed to delete a subscriber is missing.</summary>
    void EnsureConfigured();

    /// <summary>Removes the user's subscriber record from RevenueCat. Succeeds if there is none.</summary>
    Task DeleteSubscriberAsync(Guid userId, CancellationToken cancellationToken);
}
