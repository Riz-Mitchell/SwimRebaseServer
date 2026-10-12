using System.Net;
using System.Net.Http.Headers;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure;

public sealed class SupabaseAdminClient(HttpClient http, IOptions<SupabaseOptions> options) : ISupabaseAdminClient
{
    public async Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var serviceRoleKey = options.Value.ServiceRoleKey;
        if (string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            throw new InvalidOperationException(
                "Missing configuration 'Supabase:ServiceRoleKey' - needed to delete a user from Supabase Auth.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"auth/v1/admin/users/{userId}");
        request.Headers.Add("apikey", serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }
}
