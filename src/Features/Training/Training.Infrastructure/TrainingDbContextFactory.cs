using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Training.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `dotnet ef database update` construct
/// <see cref="TrainingDbContext"/> without spinning up the whole host. Real runtime configuration
/// comes from <see cref="ServiceCollectionExtensions.AddTrainingInfrastructure"/> instead.
/// </summary>
public sealed class TrainingDbContextFactory : IDesignTimeDbContextFactory<TrainingDbContext>
{
    public TrainingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseNpgsql("Host=localhost;Database=swimrebase_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new TrainingDbContext(optionsBuilder.Options);
    }
}
