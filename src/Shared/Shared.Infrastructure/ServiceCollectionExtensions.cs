using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Storage;
using Shared.Infrastructure.Storage;

namespace Shared.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires cross-feature infrastructure that isn't tied to any one feature's DbContext. Currently
    /// just blob storage (Cloudflare R2, via the S3-compatible API) - expects "CloudflareR2:AccountId"
    /// / "CloudflareR2:BucketName" (non-secret) and "CloudflareR2:AccessKeyId" /
    /// "CloudflareR2:SecretAccessKey" (from Key Vault / user-secrets).
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CloudflareR2Options>(configuration.GetSection(CloudflareR2Options.SectionName));

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = configuration.GetSection(CloudflareR2Options.SectionName).Get<CloudflareR2Options>()
                ?? new CloudflareR2Options();

            var config = new AmazonS3Config
            {
                ServiceURL = $"https://{options.AccountId}.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            };

            return new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, config);
        });

        services.AddSingleton<IBlobStorageService, R2BlobStorageService>();

        return services;
    }
}
