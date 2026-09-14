using Microsoft.EntityFrameworkCore;
using Training.Domain.Models;

namespace Training.Infrastructure;

public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options) : DbContext(options)
{
    public DbSet<TrainingUser> TrainingUsers => Set<TrainingUser>();

    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();

    public DbSet<ExpectedSession> ExpectedSessions => Set<ExpectedSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrainingDbContext).Assembly);
    }
}
