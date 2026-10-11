using Billing.Domain.Models;

namespace Billing.Application.Models;

/// <param name="Limit">Conversions allowed this week.</param>
/// <param name="ResetsAt">When the week's count starts again.</param>
public sealed record QuotaSnapshot(int Limit, int Used, DateTimeOffset ResetsAt);

public sealed record SubscriptionView(string ProductId, string Store, DateTimeOffset ExpiresAt, bool WillRenew);

public sealed record Entitlement(Plan Plan, DateTimeOffset? TrialEndsAt, SubscriptionView? Subscription, QuotaSnapshot Conversions);

public sealed record QuotaDecision(bool Allowed, QuotaSnapshot Quota);
