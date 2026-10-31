using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SessionConversion.Application;
using SessionConversion.Application.Interfaces;
using SessionConversion.Application.Services;
using SessionConversion.Infrastructure.Repositories;
using Shared.Application;
using Shared.Infrastructure;

namespace SessionConversion.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the session-conversion feature's own database and services. Expects a "Database" connection string - shared with
    /// the other features until they need to scale onto separate databases.
    /// </summary>
    public static IServiceCollection AddSessionConversionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SessionConversionDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
            .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork, UnitOfWork<SessionConversionDbContext>>(SessionConversionKeys.UnitOfWork);
        services.AddScoped<IUserDataEraser, SessionConversionUserDataEraser>();
        services.AddScoped<ISessionDraftRepository, SessionDraftRepository>();
        services.AddScoped<SessionDraftService>();
        services.AddScoped<SessionPromotionService>();

        return services;
    }
}
