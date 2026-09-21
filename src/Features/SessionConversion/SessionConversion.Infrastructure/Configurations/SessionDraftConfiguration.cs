using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SessionConversion.Domain.Models;

namespace SessionConversion.Infrastructure.Configurations;

public sealed class SessionDraftConfiguration : IEntityTypeConfiguration<SessionDraft>
{
    public void Configure(EntityTypeBuilder<SessionDraft> builder)
    {
        builder.ToTable("session_drafts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.UserId).IsRequired();
        // Plain text rather than jsonb: never queried at the SQL level, and jsonb reorders object keys, which breaks
        // polymorphic deserialization on the "kind" discriminator.
        builder.Property(d => d.ContentJson).HasColumnType("text").IsRequired();
        builder.Property(d => d.Version).IsRequired();
        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.UpdatedAt).IsRequired();

        // "This user's drafts" and "the draft an interview produced".
        builder.HasIndex(d => new { d.UserId, d.CreatedAt });
        builder.HasIndex(d => d.SourceConversationId);
    }
}
