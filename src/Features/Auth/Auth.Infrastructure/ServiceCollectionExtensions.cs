using Auth.Application;
using Auth.Application.Interfaces;
using Auth.Application.Services;
using Auth.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Shared.Application;
using Shared.Infrastructure;

namespace Auth.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Auth feature: JWT Bearer authentication against Supabase's ES256/JWKS signing keys,
    /// the auth store, and user auto-provisioning. Expects "Database" (shared with the other
    /// features until they need to scale onto separate databases) and "Supabase:Url" configuration
    /// - the handler discovers the JWKS endpoint from Supabase's OIDC metadata and handles key
    /// caching/rotation itself. Pair with <c>app.UseAuthentication()</c>/<c>UseAuthorization()</c>
    /// and <c>app.UseMiddleware&lt;Auth.Infrastructure.Middleware.UserProvisioningMiddleware&gt;()</c>
    /// in the host's request pipeline.
    /// </summary>
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Database"))
            .UseSnakeCaseNamingConvention());

        services.AddHttpContextAccessor();

        var supabaseUrl = configuration["Supabase:Url"];
        if (string.IsNullOrWhiteSpace(supabaseUrl))
        {
            throw new InvalidOperationException(
                "Missing configuration 'Supabase:Url' - set it via user-secrets or an environment " +
                "variable to the project's URL (Supabase dashboard: Project Settings > API > Project URL).");
        }

        var authority = $"{supabaseUrl.TrimEnd('/')}/auth/v1";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the raw 'sub' claim instead of letting the handler remap it onto
                // ClaimTypes.NameIdentifier, so ICurrentUserAccessor reads the same claim name
                // Supabase issues.
                options.MapInboundClaims = false;

                // Authority drives discovery of "{authority}/.well-known/openid-configuration", which
                // Supabase serves with a jwks_uri - the handler fetches, caches, and auto-refreshes
                // those signing keys itself (including on an unrecognized kid), so no manual JWKS
                // fetching/caching is needed here.
                options.Authority = authority;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = "authenticated",
                    ValidateLifetime = true,
                    // Supabase's discovery metadata advertises RS256/HS256/ES256 as generally supported,
                    // but this project's JWKS only publishes an ES256 key - pin to it explicitly so a
                    // token can't claim a different algorithm (e.g. HS256-confusion against the public key).
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                };
            });

        services.AddAuthorization();

        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddKeyedScoped<IUnitOfWork, UnitOfWork<AuthDbContext>>(AuthKeys.UnitOfWork);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();

        return services;
    }
}
