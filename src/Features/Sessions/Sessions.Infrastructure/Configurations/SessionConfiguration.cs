using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.OwnerId).IsRequired();
        builder.Property(s => s.PerformedOn).IsRequired();
        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Course).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(s => s.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.TotalDistance).HasPrecision(10, 2).IsRequired();
        // Plain text rather than jsonb: the tree is never queried at the SQL level (facts are), and jsonb reorders object keys,
        // which breaks polymorphic deserialization on the "kind" discriminator.
        builder.Property(s => s.ContentJson).HasColumnType("text").IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // Two edits of one session at once: the second save fails instead of silently overwriting the first.
        builder.Property<uint>("xmin").IsRowVersion();

        // "This user's sessions, newest first" and "the session a draft became" (unique: one draft becomes one session).
        builder.HasIndex(s => new { s.OwnerId, s.PerformedOn });
        builder.HasIndex(s => s.SourceDraftId).IsUnique();
    }
}
