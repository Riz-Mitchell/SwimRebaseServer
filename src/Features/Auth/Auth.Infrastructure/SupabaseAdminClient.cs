using System.Net;
using System.Net.Http.Headers;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure;

public sealed class SupabaseAdminClient(HttpClient http, IOptions<SupabaseOptions> options, ILogger<SupabaseAdminClient> logger) : ISupabaseAdminClient
{
    public void EnsureConfigured() => _ = ServiceRoleKey();

    public async Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var serviceRoleKey = ServiceRoleKey();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"auth/v1/admin/users/{userId}");
        request.Headers.Add("apikey", serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("[{ClassName}] Supabase had no user {UserId} to delete", nameof(SupabaseAdminClient), userId);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[{ClassName}] Supabase refused to delete user {UserId}: {StatusCode}", nameof(SupabaseAdminClient), userId, (int)response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        logger.LogInformation("[{ClassName}] Deleted user {UserId} from Supabase", nameof(SupabaseAdminClient), userId);
    }

    private string ServiceRoleKey()
    {
        var serviceRoleKey = options.Value.ServiceRoleKey;
        if (string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            logger.LogError("[{ClassName}] Cannot delete users: 'Supabase:ServiceRoleKey' is not configured", nameof(SupabaseAdminClient));
            throw new InvalidOperationException(
                "Missing configuration 'Supabase:ServiceRoleKey' - needed to delete a user from Supabase Auth.");
        }

        return serviceRoleKey;
    }
}
