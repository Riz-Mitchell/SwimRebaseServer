using Microsoft.EntityFrameworkCore;
using Training.Domain.Models;

namespace Training.Infrastructure;

public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options) : DbContext(options)
{
    public DbSet<TrainingUser> TrainingUsers => Set<TrainingUser>();

    public DbSet<Focus> Focuses => Set<Focus>();

    public DbSet<ComparisonPoint> ComparisonPoints => Set<ComparisonPoint>();

    public DbSet<FocusCheckIn> FocusCheckIns => Set<FocusCheckIn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrainingDbContext).Assembly);
    }
}
