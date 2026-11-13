namespace Billing.Application.Options;

public sealed class RevenueCatOptions
{
    public const string SectionName = "RevenueCat";

    /// <summary>The value RevenueCat sends in the webhook's Authorization header. The webhook is refused while this is empty.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>A RevenueCat v2 secret API key, used to delete a user's customer record when they delete their account.</summary>
    public string SecretApiKey { get; set; } = string.Empty;

    /// <summary>The RevenueCat project id (looks like "proj1ab2c3d4"); the v2 API addresses customers under their project.</summary>
    public string ProjectId { get; set; } = string.Empty;
}
