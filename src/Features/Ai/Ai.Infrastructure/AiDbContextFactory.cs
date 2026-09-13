using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ai.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` construct <see cref="AiDbContext"/>
/// without spinning up the whole host. The connection string here only needs to be well-formed
/// enough for the Npgsql provider to build its model - it is never actually connected to by the
/// design-time tooling for `migrations add`. Real runtime configuration comes from
/// <see cref="ServiceCollectionExtensions.AddAiInfrastructure"/> instead.
/// </summary>
public sealed class AiDbContextFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    public AiDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new AiDbContext(optionsBuilder.Options);
    }
}
