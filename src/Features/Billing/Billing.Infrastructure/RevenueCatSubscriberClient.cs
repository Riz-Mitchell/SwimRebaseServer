using System.Net;
using System.Net.Http.Headers;
using Billing.Application.Interfaces;
using Billing.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Billing.Infrastructure;

public sealed class RevenueCatSubscriberClient(HttpClient http, IOptions<RevenueCatOptions> options, ILogger<RevenueCatSubscriberClient> logger) : IRevenueCatSubscriberClient
{
    public async Task DeleteSubscriberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var secretApiKey = options.Value.SecretApiKey;
        if (string.IsNullOrWhiteSpace(secretApiKey))
        {
            logger.LogError("[{ClassName}] Cannot delete subscriber {UserId}: 'RevenueCat:SecretApiKey' is not configured", nameof(RevenueCatSubscriberClient), userId);
            throw new InvalidOperationException(
                "Missing configuration 'RevenueCat:SecretApiKey' - needed to delete a user's subscriber record from RevenueCat.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v1/subscribers/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("[{ClassName}] RevenueCat had no subscriber {UserId} to delete", nameof(RevenueCatSubscriberClient), userId);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[{ClassName}] RevenueCat refused to delete subscriber {UserId}: {StatusCode}", nameof(RevenueCatSubscriberClient), userId, (int)response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        logger.LogInformation("[{ClassName}] Deleted subscriber {UserId} from RevenueCat", nameof(RevenueCatSubscriberClient), userId);
    }
}
