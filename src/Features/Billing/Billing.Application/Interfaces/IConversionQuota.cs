using Billing.Application.Models;

namespace Billing.Application.Interfaces;

/// <summary>The weekly allowance of swim-board conversions.</summary>
public interface IConversionQuota
{
    /// <summary>Counts a run against the allowance if there is room; otherwise says no and counts nothing.</summary>
    Task<QuotaDecision> TryConsumeAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Gives back the use a conversation took. Does nothing if it took none or was already refunded.</summary>
    Task RefundAsync(Guid conversationId, CancellationToken cancellationToken);
}
