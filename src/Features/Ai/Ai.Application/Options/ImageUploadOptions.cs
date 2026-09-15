namespace Ai.Application.Options;

/// <summary>
/// Fixed policy for chat image uploads - not user-configurable, so plain constants rather than a
/// bound config section. Content types match what Anthropic's vision models accept; the size cap
/// matches Anthropic's own per-image limit, checked again here (not just trusted client-side) once
/// the object actually lands in storage.
/// </summary>
public static class ImageUploadOptions
{
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
    };

    public const long MaxSizeBytes = 5 * 1024 * 1024;

    /// <summary>
    /// How long the client has to actually PUT the bytes after requesting the upload URL. Short
    /// because it's used immediately, right after being issued.
    /// </summary>
    public static readonly TimeSpan UploadUrlExpiry = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a presigned GET URL handed to Anthropic stays valid. This is regenerated fresh from
    /// the permanent object key on every model call (see AnthropicMessageMapper) rather than stored,
    /// so it only needs to outlive one request/fetch - not the life of the conversation.
    /// </summary>
    public static readonly TimeSpan DownloadUrlExpiry = TimeSpan.FromMinutes(15);
}
