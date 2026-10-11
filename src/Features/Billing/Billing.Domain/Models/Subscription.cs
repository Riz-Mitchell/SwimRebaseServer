namespace Billing.Domain.Models;

/// <summary>What RevenueCat last told us about one user's paid access. Keyed by user: there is one subscription line.</summary>
public sealed class Subscription
{
    public Guid UserId { get; set; }

    public string ProductId { get; set; } = string.Empty;

    public string Store { get; set; } = string.Empty;

    public bool IsSandbox { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool WillRenew { get; set; }

    /// <summary>Time of the newest RevenueCat event applied, so a late-arriving older one is ignored.</summary>
    public DateTimeOffset LastEventAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
