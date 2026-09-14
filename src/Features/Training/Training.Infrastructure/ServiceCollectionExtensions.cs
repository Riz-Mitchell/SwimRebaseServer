using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Training.Application.Interfaces;
using Training.Application.Services;
using Training.Infrastructure.Persistence;
using Training.Infrastructure.Repositories;

namespace Training.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Wires the Training feature's own database and services. Expects a "TrainingDatabase" connection string.</summary>
    public static IServiceCollection AddTrainingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TrainingDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("TrainingDatabase"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITrainingUserRepository, TrainingUserRepository>();
        services.AddScoped<ITrainingUserProvisioningService, TrainingUserProvisioningService>();
        services.AddScoped<ITimetableRepository, TimetableRepository>();
        services.AddScoped<TimetableService>();

        return services;
    }
}
