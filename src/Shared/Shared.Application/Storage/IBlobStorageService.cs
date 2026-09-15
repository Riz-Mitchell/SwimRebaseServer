namespace Shared.Application.Storage;

/// <summary>
/// Generic object storage capability (implemented against Cloudflare R2 in Shared.Infrastructure) -
/// deliberately provider-agnostic so no feature ever references R2/S3 types directly. Every URL this
/// returns is presigned and time-limited; nothing here is ever a permanently public link.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// A presigned URL the client can PUT the object's bytes to directly, without the request ever
    /// passing through our API. The caller is responsible for confirming the upload actually
    /// happened afterward (see <see cref="TryGetMetadataAsync"/>) - a presigned PUT URL alone gives
    /// no server-side signal that anything was ever uploaded to it.
    /// </summary>
    PresignedUploadUrl CreatePresignedUploadUrl(string objectKey, string contentType, TimeSpan expiry);

    /// <summary>
    /// A presigned URL to read the object, valid for <paramref name="expiry"/>. Generated fresh on
    /// every use (e.g. once per model turn) rather than stored, so storage of the URL itself never
    /// goes stale - only the permanent <paramref name="objectKey"/> needs to be kept.
    /// </summary>
    string CreatePresignedDownloadUrl(string objectKey, TimeSpan expiry);

    /// <summary>Null if nothing has actually been uploaded to this key yet.</summary>
    Task<BlobMetadata?> TryGetMetadataAsync(string objectKey, CancellationToken cancellationToken);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record PresignedUploadUrl(string Url, DateTimeOffset ExpiresAt);

public sealed record BlobMetadata(long SizeBytes, string? ContentType);
