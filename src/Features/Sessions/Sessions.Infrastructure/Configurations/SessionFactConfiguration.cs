using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class SessionFactConfiguration : IEntityTypeConfiguration<SessionFact>
{
    public void Configure(EntityTypeBuilder<SessionFact> builder)
    {
        builder.ToTable("session_facts");
        // A surrogate key: facts are deleted and re-inserted together on every edit, and a natural key would collide inside one save.
        builder.HasKey(f => f.Id);

        builder.Property(f => f.NodeId).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Stroke).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.Mode).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.Equip).IsRequired();
        builder.Property(f => f.Distance).IsRequired();
        builder.Property(f => f.OwnerId).IsRequired();
        builder.Property(f => f.PerformedOn).IsRequired();
        builder.Property(f => f.Unit).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(f => f.Course).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(f => f.Source).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasOne<Session>().WithMany().HasForeignKey(f => f.SessionId).OnDelete(DeleteBehavior.Cascade);

        // Every metric starts with "this user, this date range", then narrows by stroke or intensity.
        builder.HasIndex(f => new { f.OwnerId, f.PerformedOn });
        builder.HasIndex(f => new { f.OwnerId, f.Stroke, f.PerformedOn });
        builder.HasIndex(f => new { f.OwnerId, f.Intensity, f.PerformedOn });
        builder.HasIndex(f => f.SessionId);
        builder.HasIndex(f => f.Equip).HasMethod("gin");
    }
}
