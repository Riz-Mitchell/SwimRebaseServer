namespace Billing.Application.Interfaces;

public interface IRevenueCatSubscriberClient
{
    /// <summary>Removes the user's subscriber record from RevenueCat. Succeeds if there is none.</summary>
    Task DeleteSubscriberAsync(Guid userId, CancellationToken cancellationToken);
}
