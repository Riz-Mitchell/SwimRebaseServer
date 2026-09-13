using System.Text;
using Auth.Application.Interfaces;
using Auth.Application.Services;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Auth feature: JWT Bearer authentication against Supabase's shared HS256 secret,
    /// the auth store, and user auto-provisioning. Expects "AuthDatabase" and "Supabase:JwtSecret"
    /// configuration. Pair with <c>app.UseAuthentication()</c>/<c>UseAuthorization()</c> and
    /// <c>app.UseMiddleware&lt;Auth.Infrastructure.Middleware.UserProvisioningMiddleware&gt;()</c>
    /// in the host's request pipeline.
    /// </summary>
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuthDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("AuthDatabase"))
            .UseSnakeCaseNamingConvention());

        services.AddHttpContextAccessor();

        var supabaseJwtSecret = configuration["Supabase:JwtSecret"];
        if (string.IsNullOrWhiteSpace(supabaseJwtSecret))
        {
            throw new InvalidOperationException(
                "Missing configuration 'Supabase:JwtSecret' - set it via user-secrets or an environment " +
                "variable to the project's JWT secret (Supabase dashboard: Project Settings > API > JWT Settings).");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the raw 'sub' claim instead of letting the handler remap it onto
                // ClaimTypes.NameIdentifier, so ICurrentUserAccessor reads the same claim name
                // Supabase issues.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseJwtSecret)),
                    ValidateIssuer = false,
                    ValidateAudience = true,
                    ValidAudience = "authenticated",
                    ValidateLifetime = true,
                };
            });

        services.AddAuthorization();

        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();

        return services;
    }
}
