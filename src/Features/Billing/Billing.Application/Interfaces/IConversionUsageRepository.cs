using Billing.Domain.Models;

namespace Billing.Application.Interfaces;

/// <summary>Pure query/tracking against the billing store - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface IConversionUsageRepository
{
    /// <summary>Holds a lock on this user until the open transaction ends, so two requests cannot both pass the same limit check.</summary>
    Task LockUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Counted uses since <paramref name="since"/>; refunded ones are left out.</summary>
    Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken);

    Task<ConversionUsage?> FindByConversationAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(ConversionUsage usage);
}
