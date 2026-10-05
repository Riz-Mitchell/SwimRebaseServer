using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class SessionPreferencesConfiguration : IEntityTypeConfiguration<SessionPreferences>
{
    public void Configure(EntityTypeBuilder<SessionPreferences> builder)
    {
        builder.ToTable("session_preferences");
        builder.HasKey(p => p.OwnerId);
        builder.Property(p => p.OwnerId).ValueGeneratedNever();
        builder.Property(p => p.DefaultEffort).IsRequired();
        builder.Property(p => p.EffortScale).IsRequired().HasMaxLength(32).HasDefaultValue(EffortScale.TensId);
        builder.Property(p => p.CustomEffortScaleJson);
    }
}
