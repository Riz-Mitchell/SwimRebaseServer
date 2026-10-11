using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Billing.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` construct
/// <see cref="BillingDbContext"/> without spinning up the whole host. Real runtime configuration
/// comes from <see cref="ServiceCollectionExtensions.AddBillingInfrastructure"/> instead.
/// </summary>
public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new BillingDbContext(optionsBuilder.Options);
    }
}
