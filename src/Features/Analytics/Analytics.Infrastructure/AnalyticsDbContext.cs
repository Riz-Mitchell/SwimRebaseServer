using Analytics.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Infrastructure;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<EventLog> EventLogs => Set<EventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);
    }
}
