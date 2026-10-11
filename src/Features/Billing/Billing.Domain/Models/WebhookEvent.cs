namespace Billing.Domain.Models;

/// <summary>A RevenueCat event already processed; RevenueCat retries, so its id is the idempotency key.</summary>
public sealed class WebhookEvent
{
    public string EventId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }
}
