using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sessions.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` construct <see cref="SessionsDbContext"/> without spinning up the whole host. The connection
/// string only needs to be well-formed for the Npgsql provider to build its model - design-time tooling never connects for `migrations add`.
/// </summary>
public sealed class SessionsDbContextFactory : IDesignTimeDbContextFactory<SessionsDbContext>
{
    public SessionsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SessionsDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new SessionsDbContext(optionsBuilder.Options);
    }
}
