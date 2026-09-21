using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SessionConversion.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` construct <see cref="SessionConversionDbContext"/> without spinning up the whole host. The connection
/// string only needs to be well-formed for the Npgsql provider to build its model - design-time tooling never connects for `migrations add`.
/// </summary>
public sealed class SessionConversionDbContextFactory : IDesignTimeDbContextFactory<SessionConversionDbContext>
{
    public SessionConversionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SessionConversionDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new SessionConversionDbContext(optionsBuilder.Options);
    }
}
