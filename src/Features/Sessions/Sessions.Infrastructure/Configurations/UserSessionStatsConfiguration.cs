using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class UserSessionStatsConfiguration : IEntityTypeConfiguration<UserSessionStats>
{
    public void Configure(EntityTypeBuilder<UserSessionStats> builder)
    {
        builder.ToTable("session_stats");
        builder.HasKey(s => s.OwnerId);
        builder.Property(s => s.OwnerId).ValueGeneratedNever();
        builder.Property(s => s.SessionCount).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();
    }
}
