using Billing.Application;
using Billing.Application.Interfaces;
using Billing.Application.Options;
using Billing.Application.Services;
using Billing.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
using Shared.Infrastructure;

namespace Billing.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Billing feature's own database and services. Expects a "Database" connection
    /// string - shared with the other features until they need to scale onto separate databases.
    /// </summary>
    public static IServiceCollection AddBillingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BillingDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Database"))
            .UseSnakeCaseNamingConvention());

        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));
        services.Configure<RevenueCatOptions>(configuration.GetSection(RevenueCatOptions.SectionName));

        services.AddKeyedScoped<IUnitOfWork, UnitOfWork<BillingDbContext>>(BillingKeys.UnitOfWork);
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IConversionUsageRepository, ConversionUsageRepository>();
        services.AddScoped<ITrialReader, ProfileTrialReader>();
        services.AddScoped<EntitlementService>();
        services.AddScoped<IConversionQuota, ConversionQuotaService>();
        services.AddScoped<RevenueCatWebhookHandler>();

        services.AddHttpClient<IRevenueCatSubscriberClient, RevenueCatSubscriberClient>(client =>
            client.BaseAddress = new Uri("https://api.revenuecat.com/"));
        services.AddScoped<IUserDataEraser, BillingUserDataEraser>();

        return services;
    }
}
