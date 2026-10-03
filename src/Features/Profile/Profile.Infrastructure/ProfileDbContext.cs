using Microsoft.EntityFrameworkCore;
using Profile.Domain.Models;

namespace Profile.Infrastructure;

public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<ProfileAnswer> ProfileAnswers => Set<ProfileAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProfileDbContext).Assembly);
    }
}
