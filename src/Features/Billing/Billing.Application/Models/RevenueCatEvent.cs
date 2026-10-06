using System.Text.Json.Serialization;

namespace Billing.Application.Models;

/// <summary>The body RevenueCat posts to the webhook; only the fields used here.</summary>
public sealed record RevenueCatWebhook([property: JsonPropertyName("event")] RevenueCatEvent? Event);

public sealed record RevenueCatEvent(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("app_user_id")] string? AppUserId,
    [property: JsonPropertyName("product_id")] string? ProductId,
    [property: JsonPropertyName("store")] string? Store,
    [property: JsonPropertyName("environment")] string? Environment,
    [property: JsonPropertyName("event_timestamp_ms")] long? EventTimestampMs,
    [property: JsonPropertyName("expiration_at_ms")] long? ExpirationAtMs,
    [property: JsonPropertyName("transferred_from")] string[]? TransferredFrom,
    [property: JsonPropertyName("transferred_to")] string[]? TransferredTo);

public enum WebhookResult { Applied, Duplicate, Ignored }
