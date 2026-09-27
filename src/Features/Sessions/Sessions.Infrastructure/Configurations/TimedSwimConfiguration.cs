using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class TimedSwimConfiguration : IEntityTypeConfiguration<TimedSwim>
{
    public void Configure(EntityTypeBuilder<TimedSwim> builder)
    {
        builder.ToTable("timed_swims");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.PerformedOn).IsRequired();
        builder.Property(t => t.Course).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(t => t.Dist).IsRequired();
        builder.Property(t => t.Stroke).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Mode).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Seconds).IsRequired();
        builder.Property(t => t.Effort).HasColumnType("text");
        builder.Property(t => t.StartType).HasConversion<string>().HasMaxLength(8);
        builder.Property(t => t.Finish).HasConversion<string>().HasMaxLength(8);
        builder.Property(t => t.ResistanceType).HasMaxLength(64);
        builder.Property(t => t.ResistanceLoad).HasMaxLength(64);
        // Plain text, not jsonb: splits are read as a whole with the swim, never queried at the SQL level.
        builder.Property(t => t.SplitsJson).HasColumnType("text").IsRequired();
        builder.Property(t => t.IncludeInMetrics).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        // Detaching, not cascading: deleting the session a timed swim was logged during shouldn't destroy the swim's own record.
        builder.HasOne<Session>().WithMany().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.OwnerId, t.PerformedOn });
        builder.HasIndex(t => new { t.OwnerId, t.Dist, t.Stroke, t.PerformedOn });
        builder.HasIndex(t => t.SessionId);
    }
}
