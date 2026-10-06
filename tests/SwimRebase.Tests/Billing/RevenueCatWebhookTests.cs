using Billing.Application.Models;
using Billing.Domain.Models;

namespace SwimRebase.Tests.Payments;

public class RevenueCatWebhookTests
{
    private static readonly long Now = BillingHarness.Start.ToUnixTimeMilliseconds();
    private static readonly long NextMonth = BillingHarness.Start.AddDays(30).ToUnixTimeMilliseconds();

    private readonly BillingHarness h = new();

    private static RevenueCatEvent Event(
        string id, string type, string? user = null, long? at = null, long? expires = null, string environment = "PRODUCTION",
        string[]? from = null, string[]? to = null) =>
        new(id, type, user ?? BillingHarness.UserId.ToString(), "pro_monthly", "APP_STORE", environment, at ?? Now, expires ?? NextMonth, from, to);

    private Task<WebhookResult> SendAsync(RevenueCatEvent e) => h.Webhook.HandleAsync(e, CancellationToken.None);

    private Subscription? Stored => h.Subscriptions.Subscriptions.GetValueOrDefault(BillingHarness.UserId);

    [Fact]
    public async Task A_purchase_grants_access_until_the_expiry_and_will_renew()
    {
        Assert.Equal(WebhookResult.Applied, await SendAsync(Event("e1", "INITIAL_PURCHASE")));

        Assert.Equal(BillingHarness.Start.AddDays(30), Stored!.ExpiresAt);
        Assert.True(Stored.WillRenew);
        Assert.Equal("pro_monthly", Stored.ProductId);
        Assert.False(Stored.IsSandbox);
    }

    [Fact]
    public async Task A_cancellation_keeps_access_to_the_end_of_the_paid_period_but_stops_renewing()
    {
        await SendAsync(Event("e1", "INITIAL_PURCHASE"));
        await SendAsync(Event("e2", "CANCELLATION", at: Now + 1000));

        Assert.False(Stored!.WillRenew);
        Assert.Equal(BillingHarness.Start.AddDays(30), Stored.ExpiresAt);
        Assert.Equal(Plan.Pro, (await h.Entitlements.GetAsync(BillingHarness.UserId, CancellationToken.None)).Plan);
    }

    [Fact]
    public async Task Un_cancelling_turns_renewal_back_on()
    {
        await SendAsync(Event("e1", "CANCELLATION"));
        await SendAsync(Event("e2", "UNCANCELLATION", at: Now + 1000));

        Assert.True(Stored!.WillRenew);
    }

    [Fact]
    public async Task An_expiration_ends_access_at_the_event_expiry()
    {
        await SendAsync(Event("e1", "INITIAL_PURCHASE"));
        var expired = Now - 60_000;
        await SendAsync(Event("e2", "EXPIRATION", at: Now + 1000, expires: expired));

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(expired), Stored!.ExpiresAt);
        Assert.Equal(Plan.Free, (await h.Entitlements.GetAsync(BillingHarness.UserId, CancellationToken.None)).Plan);
    }

    [Fact]
    public async Task The_same_event_delivered_twice_is_applied_once()
    {
        await SendAsync(Event("e1", "INITIAL_PURCHASE"));
        Stored!.WillRenew = false;

        Assert.Equal(WebhookResult.Duplicate, await SendAsync(Event("e1", "INITIAL_PURCHASE")));
        Assert.False(Stored.WillRenew);
    }

    [Fact]
    public async Task An_older_event_arriving_late_does_not_overwrite_a_newer_one()
    {
        await SendAsync(Event("e2", "RENEWAL", at: Now + 5000, expires: NextMonth + 100_000));

        await SendAsync(Event("e1", "INITIAL_PURCHASE", at: Now, expires: NextMonth));

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(NextMonth + 100_000), Stored!.ExpiresAt);
    }

    [Fact]
    public async Task A_sandbox_event_is_stored_as_sandbox()
    {
        await SendAsync(Event("e1", "INITIAL_PURCHASE", environment: "SANDBOX"));

        Assert.True(Stored!.IsSandbox);
    }

    [Fact]
    public async Task An_anonymous_revenuecat_user_is_accepted_and_dropped()
    {
        var result = await SendAsync(Event("e1", "INITIAL_PURCHASE", user: "$RCAnonymousID:abc123"));

        Assert.Equal(WebhookResult.Ignored, result);
        Assert.Empty(h.Subscriptions.Subscriptions);
    }

    [Theory]
    [InlineData("TEST")]
    [InlineData("BILLING_ISSUE")]
    [InlineData("SOMETHING_NEW")]
    public async Task Events_that_do_not_change_access_are_ignored(string type)
    {
        Assert.Equal(WebhookResult.Ignored, await SendAsync(Event("e1", type)));
        Assert.Empty(h.Subscriptions.Subscriptions);
    }

    [Fact]
    public async Task A_transfer_moves_the_subscription_to_the_new_user()
    {
        var other = Guid.NewGuid();
        await SendAsync(Event("e1", "INITIAL_PURCHASE", user: other.ToString()));

        var result = await SendAsync(Event("e2", "TRANSFER", user: null, at: Now + 1000, from: [other.ToString()], to: [BillingHarness.UserId.ToString()]));

        Assert.Equal(WebhookResult.Applied, result);
        Assert.False(h.Subscriptions.Subscriptions.ContainsKey(other));
        Assert.Equal(BillingHarness.Start.AddDays(30), Stored!.ExpiresAt);
    }

    [Fact]
    public async Task A_transfer_from_someone_with_no_subscription_changes_nothing()
    {
        var result = await SendAsync(Event("e1", "TRANSFER", from: [Guid.NewGuid().ToString()], to: [BillingHarness.UserId.ToString()]));

        Assert.Equal(WebhookResult.Ignored, result);
        Assert.Empty(h.Subscriptions.Subscriptions);
    }

    [Fact]
    public async Task An_event_with_no_id_is_ignored()
    {
        Assert.Equal(WebhookResult.Ignored, await SendAsync(Event(string.Empty, "INITIAL_PURCHASE")));
    }
}
