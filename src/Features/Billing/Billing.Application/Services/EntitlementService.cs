using Billing.Application.Interfaces;
using Billing.Application.Models;
using Billing.Application.Options;
using Billing.Domain.Models;
using Microsoft.Extensions.Options;

namespace Billing.Application.Services;

/// <summary>Works out what a user may do: paid beats trial beats free, and each plan has a weekly conversion limit.</summary>
public sealed class EntitlementService(
    ISubscriptionRepository subscriptions,
    IConversionUsageRepository usage,
    ITrialReader trials,
    IOptions<BillingOptions> options,
    TimeProvider clock)
{
    public async Task<Entitlement> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (plan, trialEndsAt, subscription) = await ResolveAsync(userId, cancellationToken);
        var used = await usage.CountSinceAsync(userId, WeekStart(clock.GetUtcNow()), cancellationToken);
        return new Entitlement(plan, trialEndsAt, plan == Plan.Pro && subscription is not null ? ToView(subscription) : null, Snapshot(plan, used));
    }

    internal async Task<(Plan Plan, DateTimeOffset? TrialEndsAt, Subscription? Subscription)> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var subscription = await subscriptions.FindAsync(userId, cancellationToken);
        var trialEndsAt = await trials.TrialEndsAtAsync(userId, cancellationToken);

        if (subscription is not null && subscription.ExpiresAt > now && (!subscription.IsSandbox || options.Value.AcceptSandbox))
        {
            return (Plan.Pro, trialEndsAt, subscription);
        }

        return (trialEndsAt > now ? Plan.Trial : Plan.Free, trialEndsAt, subscription);
    }

    internal QuotaSnapshot Snapshot(Plan plan, int used) =>
        new(plan == Plan.Free ? options.Value.FreeWeeklyConversions : options.Value.FairUseWeeklyConversions, used, WeekStart(clock.GetUtcNow()).AddDays(7));

    /// <summary>Weeks run Monday to Sunday in UTC, like the metrics weeks.</summary>
    internal static DateTimeOffset WeekStart(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return new DateTimeOffset(monday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static SubscriptionView ToView(Subscription s) => new(s.ProductId, s.Store, s.ExpiresAt, s.WillRenew);
}
