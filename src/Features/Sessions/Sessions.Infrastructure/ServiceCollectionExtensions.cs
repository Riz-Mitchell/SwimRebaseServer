using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sessions.Application;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Infrastructure.Repositories;
using Shared.Application;
using Shared.Infrastructure;

namespace Sessions.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the sessions feature's own database and services. Expects a "Database" connection string - shared with
    /// the other features until they need to scale onto separate databases.
    /// </summary>
    public static IServiceCollection AddSessionsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SessionsDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Database"))
            .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork, UnitOfWork<SessionsDbContext>>(SessionsKeys.UnitOfWork);
        services.AddScoped<IUserDataEraser, SessionsUserDataEraser>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IMetricsRepository, MetricsRepository>();
        services.AddScoped<ITimedSwimRepository, TimedSwimRepository>();
        services.AddScoped<ISessionPreferencesRepository, SessionPreferencesRepository>();
        services.AddScoped<IUserEffortTermRepository, UserEffortTermRepository>();
        services.AddScoped<ICustomMetricRepository, CustomMetricRepository>();

        // One mapper per external intensity scale; add a scale by registering another IIntensityMapper.
        services.AddSingleton<IIntensityMapper, VocabularyIntensityMapper>();
        services.AddSingleton<IntensityResolver>();
        services.AddSingleton<SessionIngest>();
        services.AddScoped<IntensityBackfill>();

        services.AddScoped<SessionService>();
        services.AddScoped<MetricsService>();
        services.AddScoped<CustomMetricEvaluator>();
        services.AddScoped<CustomMetricService>();
        services.AddScoped<TimedSwimService>();
        services.AddScoped<SessionPreferencesService>();
        services.AddScoped<IProgressionDefaultsProvider>(sp => sp.GetRequiredService<SessionPreferencesService>());
        services.AddScoped<UserEffortTermService>();
        services.AddScoped<IPersonalIntensityProvider>(sp => sp.GetRequiredService<UserEffortTermService>());
        services.AddScoped<IEffortTermLearning>(sp => sp.GetRequiredService<UserEffortTermService>());

        return services;
    }
}
