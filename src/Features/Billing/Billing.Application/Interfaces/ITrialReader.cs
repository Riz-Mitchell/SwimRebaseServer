namespace Billing.Application.Interfaces;

public interface ITrialReader
{
    /// <summary>When the user's free trial ends, or null if it has not started.</summary>
    Task<DateTimeOffset?> TrialEndsAtAsync(Guid userId, CancellationToken cancellationToken);
}
