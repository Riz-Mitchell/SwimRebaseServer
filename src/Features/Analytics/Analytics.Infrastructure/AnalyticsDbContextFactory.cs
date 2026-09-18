using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Analytics.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` construct <see cref="AnalyticsDbContext"/>
/// without spinning up the whole host. The connection string here only needs to be well-formed
/// enough for the Npgsql provider to build its model - it is never actually connected to by the
/// design-time tooling for `migrations add`. Real runtime configuration comes from
/// <see cref="ServiceCollectionExtensions.AddAnalyticsInfrastructure"/> instead.
/// </summary>
public sealed class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new AnalyticsDbContext(optionsBuilder.Options);
    }
}
