using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Shared.Infrastructure.Database;

public static class DatabaseExtensions
{
    private static readonly string[] TokenScopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    /// <summary>
    /// One data source shared by every feature's DbContext. A "Database" connection string without a
    /// password authenticates as the app's Entra identity; one with a password is used as given.
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(configuration.GetConnectionString("Database"));

            if (string.IsNullOrEmpty(builder.ConnectionStringBuilder.Password))
            {
                var credential = new DefaultAzureCredential();
                builder.UsePeriodicPasswordProvider(
                    async (_, cancellationToken) => (await credential.GetTokenAsync(new TokenRequestContext(TokenScopes), cancellationToken)).Token,
                    successRefreshInterval: TimeSpan.FromMinutes(55),
                    failureRefreshInterval: TimeSpan.FromSeconds(5));
            }

            return builder.Build();
        });

        return services;
    }
}
