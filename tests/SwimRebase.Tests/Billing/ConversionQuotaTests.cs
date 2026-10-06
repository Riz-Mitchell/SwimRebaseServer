namespace SwimRebase.Tests.Payments;

public class ConversionQuotaTests
{
    private readonly BillingHarness h = new();

    private Task<Billing.Application.Models.QuotaDecision> TryAsync(Guid? conversationId = null) =>
        h.Quota.TryConsumeAsync(BillingHarness.UserId, conversationId ?? Guid.NewGuid(), CancellationToken.None);

    [Fact]
    public async Task A_free_user_gets_two_conversions_a_week_then_is_refused_without_anything_being_counted()
    {
        Assert.True((await TryAsync()).Allowed);
        var second = await TryAsync();
        Assert.True(second.Allowed);
        Assert.Equal(2, second.Quota.Used);

        var third = await TryAsync();

        Assert.False(third.Allowed);
        Assert.Equal(2, third.Quota.Limit);
        Assert.Equal(2, h.Usage.Usages.Count);
    }

    [Fact]
    public async Task The_count_starts_again_on_monday_utc()
    {
        await TryAsync();
        await TryAsync();
        Assert.False((await TryAsync()).Allowed);

        h.Clock.Now = new DateTimeOffset(2026, 10, 11, 23, 59, 0, TimeSpan.Zero);
        Assert.False((await TryAsync()).Allowed);

        h.Clock.Now = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        Assert.True((await TryAsync()).Allowed);
    }

    [Fact]
    public async Task A_subscriber_is_held_to_the_fair_use_limit_not_the_free_one()
    {
        h.GivePaid(BillingHarness.Start.AddDays(20));
        h.Options.FairUseWeeklyConversions = 3;

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await TryAsync()).Allowed);
        }

        Assert.False((await TryAsync()).Allowed);
    }

    [Fact]
    public async Task A_refund_frees_the_slot_and_is_only_given_once()
    {
        var first = Guid.NewGuid();
        await TryAsync(first);
        await TryAsync();
        Assert.False((await TryAsync()).Allowed);

        await h.Quota.RefundAsync(first, CancellationToken.None);
        await h.Quota.RefundAsync(first, CancellationToken.None);

        Assert.True((await TryAsync()).Allowed);
        Assert.False((await TryAsync()).Allowed);
    }

    [Fact]
    public async Task Refunding_a_conversation_that_was_never_counted_does_nothing()
    {
        await h.Quota.RefundAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(h.Usage.Usages);
    }

    [Fact]
    public async Task Another_users_conversions_do_not_count()
    {
        await h.Quota.TryConsumeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await h.Quota.TryConsumeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True((await TryAsync()).Allowed);
    }
}
