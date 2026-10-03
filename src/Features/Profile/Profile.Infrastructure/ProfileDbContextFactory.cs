using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Profile.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` construct
/// <see cref="ProfileDbContext"/> without spinning up the whole host. Real runtime configuration
/// comes from <see cref="ServiceCollectionExtensions.AddProfileInfrastructure"/> instead.
/// </summary>
public sealed class ProfileDbContextFactory : IDesignTimeDbContextFactory<ProfileDbContext>
{
    public ProfileDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new ProfileDbContext(optionsBuilder.Options);
    }
}
