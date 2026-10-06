using Billing.Domain.Models;

namespace SwimRebase.Tests.Payments;

public class EntitlementTests
{
    private readonly BillingHarness h = new();

    private Task<Billing.Application.Models.Entitlement> GetAsync() => h.Entitlements.GetAsync(BillingHarness.UserId, CancellationToken.None);

    [Fact]
    public async Task A_user_with_no_subscription_and_no_trial_is_free()
    {
        var e = await GetAsync();

        Assert.Equal(Plan.Free, e.Plan);
        Assert.Equal(2, e.Conversions.Limit);
        Assert.Null(e.Subscription);
    }

    [Fact]
    public async Task A_user_inside_their_trial_is_on_the_trial_with_the_fair_use_limit()
    {
        h.Trial.EndsAt = BillingHarness.Start.AddDays(5);

        var e = await GetAsync();

        Assert.Equal(Plan.Trial, e.Plan);
        Assert.Equal(50, e.Conversions.Limit);
        Assert.Equal(h.Trial.EndsAt, e.TrialEndsAt);
    }

    [Fact]
    public async Task A_trial_that_has_just_ended_is_free()
    {
        h.Trial.EndsAt = BillingHarness.Start;

        Assert.Equal(Plan.Free, (await GetAsync()).Plan);
    }

    [Fact]
    public async Task A_paid_subscription_beats_a_running_trial()
    {
        h.Trial.EndsAt = BillingHarness.Start.AddDays(5);
        h.GivePaid(BillingHarness.Start.AddDays(20));

        var e = await GetAsync();

        Assert.Equal(Plan.Pro, e.Plan);
        Assert.Equal("pro_monthly", e.Subscription!.ProductId);
    }

    [Fact]
    public async Task An_expired_subscription_falls_back_to_the_trial_or_free()
    {
        h.GivePaid(BillingHarness.Start.AddSeconds(-1));
        Assert.Equal(Plan.Free, (await GetAsync()).Plan);

        h.Trial.EndsAt = BillingHarness.Start.AddDays(1);
        Assert.Equal(Plan.Trial, (await GetAsync()).Plan);
    }

    [Fact]
    public async Task A_sandbox_purchase_grants_nothing_unless_sandbox_is_accepted()
    {
        h.GivePaid(BillingHarness.Start.AddDays(20), sandbox: true);
        Assert.Equal(Plan.Free, (await GetAsync()).Plan);

        h.Options.AcceptSandbox = true;
        Assert.Equal(Plan.Pro, (await GetAsync()).Plan);
    }

    [Fact]
    public async Task The_week_runs_from_monday_utc_and_resets_the_following_monday()
    {
        var e = await GetAsync();

        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), e.Conversions.ResetsAt);
    }
}
