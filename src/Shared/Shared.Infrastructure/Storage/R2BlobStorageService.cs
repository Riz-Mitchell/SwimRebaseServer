using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Shared.Application.Storage;

namespace Shared.Infrastructure.Storage;

/// <summary>
/// <see cref="IBlobStorageService"/> against Cloudflare R2, which speaks the S3 API - so this just
/// points the AWS S3 SDK at R2's endpoint (see <see cref="ServiceCollectionExtensions.AddSharedInfrastructure"/>
/// for the client setup) rather than pulling in an R2-specific SDK.
/// </summary>
public sealed class R2BlobStorageService(IAmazonS3 s3, IOptions<CloudflareR2Options> options) : IBlobStorageService
{
    private readonly DownloadUrlMemo _downloadUrls = new(TimeProvider.System);

    private string BucketName => options.Value.BucketName;

    public PresignedUploadUrl CreatePresignedUploadUrl(string objectKey, string contentType, TimeSpan expiry)
    {
        var expiresAt = DateTime.UtcNow.Add(expiry);
        var url = s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = contentType,
        });

        return new PresignedUploadUrl(url, expiresAt);
    }

    public string CreatePresignedDownloadUrl(string objectKey, TimeSpan expiry) =>
        _downloadUrls.GetOrCreate(objectKey, expiry, () =>
            s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(expiry),
            }));

    public async Task<BlobMetadata?> TryGetMetadataAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = BucketName, Key = objectKey }, cancellationToken);
            return new BlobMetadata(response.ContentLength, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = objectKey }, cancellationToken);
}
