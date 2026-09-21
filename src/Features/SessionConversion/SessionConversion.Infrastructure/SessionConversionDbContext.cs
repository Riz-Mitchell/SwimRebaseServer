using Microsoft.EntityFrameworkCore;
using SessionConversion.Domain.Models;

namespace SessionConversion.Infrastructure;

public sealed class SessionConversionDbContext(DbContextOptions<SessionConversionDbContext> options) : DbContext(options)
{
    public DbSet<SessionDraft> SessionDrafts => Set<SessionDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SessionConversionDbContext).Assembly);
    }
}
