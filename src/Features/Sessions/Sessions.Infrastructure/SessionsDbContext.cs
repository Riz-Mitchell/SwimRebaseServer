using Microsoft.EntityFrameworkCore;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure;

public sealed class SessionsDbContext(DbContextOptions<SessionsDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<SessionFact> SessionFacts => Set<SessionFact>();

    public DbSet<TimedSwim> TimedSwims => Set<TimedSwim>();

    public DbSet<SessionPreferences> SessionPreferences => Set<SessionPreferences>();

    public DbSet<UserEffortTerm> UserEffortTerms => Set<UserEffortTerm>();

    public DbSet<CustomMetric> CustomMetrics => Set<CustomMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SessionsDbContext).Assembly);
    }
}
