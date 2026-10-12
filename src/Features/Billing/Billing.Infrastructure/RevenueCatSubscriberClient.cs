using System.Net;
using System.Net.Http.Headers;
using Billing.Application.Interfaces;
using Billing.Application.Options;
using Microsoft.Extensions.Options;

namespace Billing.Infrastructure;

public sealed class RevenueCatSubscriberClient(HttpClient http, IOptions<RevenueCatOptions> options) : IRevenueCatSubscriberClient
{
    public async Task DeleteSubscriberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var secretApiKey = options.Value.SecretApiKey;
        if (string.IsNullOrWhiteSpace(secretApiKey))
        {
            throw new InvalidOperationException(
                "Missing configuration 'RevenueCat:SecretApiKey' - needed to delete a user's subscriber record from RevenueCat.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v1/subscribers/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }
}
