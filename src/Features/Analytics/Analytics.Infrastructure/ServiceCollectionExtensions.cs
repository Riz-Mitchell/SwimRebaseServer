using Analytics.Application.Interfaces;
using Analytics.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Analytics.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Analytics feature's persistence and event-tracking/reporting services. Expects a
    /// "Database" connection string (shared with the other features, same as everywhere else).
    /// </summary>
    public static IServiceCollection AddAnalyticsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AnalyticsDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Database"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IEventTracker, EventTracker>();
        services.AddScoped<IEventAnalyticsService, EventAnalyticsService>();

        return services;
    }
}
