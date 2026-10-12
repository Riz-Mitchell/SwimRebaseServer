namespace Billing.Application.Options;

public sealed class RevenueCatOptions
{
    public const string SectionName = "RevenueCat";

    /// <summary>The value RevenueCat sends in the webhook's Authorization header. The webhook is refused while this is empty.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>A RevenueCat secret API key, used to delete a user's subscriber record when they delete their account.</summary>
    public string SecretApiKey { get; set; } = string.Empty;
}
