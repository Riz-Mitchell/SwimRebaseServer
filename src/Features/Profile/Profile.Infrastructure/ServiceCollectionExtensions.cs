using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Profile.Application;
using Profile.Application.Interfaces;
using Profile.Application.Services;
using Profile.Infrastructure.Repositories;
using Shared.Application;
using Shared.Infrastructure;

namespace Profile.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Profile feature's own database and services. Expects a "Database" connection
    /// string - shared with the other features until they need to scale onto separate databases.
    /// </summary>
    public static IServiceCollection AddProfileInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProfileDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Database"))
            .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork, UnitOfWork<ProfileDbContext>>(ProfileKeys.UnitOfWork);
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<OnboardingService>();

        return services;
    }
}
