namespace Shared.Infrastructure.Storage;

/// <summary>
/// Bound from the "CloudflareR2" config section. AccountId/BucketName are non-secret (committed in
/// appsettings.json); AccessKeyId/SecretAccessKey are R2 API token credentials and belong in Key
/// Vault / user-secrets only, same as every other secret in this app (see Program.cs's Key Vault wiring).
/// </summary>
public sealed class CloudflareR2Options
{
    public const string SectionName = "CloudflareR2";

    public string AccountId { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;
}
