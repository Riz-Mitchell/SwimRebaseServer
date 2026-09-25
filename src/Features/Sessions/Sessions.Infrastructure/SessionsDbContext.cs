using Microsoft.EntityFrameworkCore;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure;

public sealed class SessionsDbContext(DbContextOptions<SessionsDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<SessionFact> SessionFacts => Set<SessionFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SessionsDbContext).Assembly);
    }
}
