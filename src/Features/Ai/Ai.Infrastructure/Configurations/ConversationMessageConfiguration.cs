using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ai.Infrastructure.Configurations;

public sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.RawContentJson).HasColumnType("jsonb");
        builder.Property(m => m.CreatedAt).IsRequired();

        // Enforces ordering per conversation and backs the "live (non-compacted) context" query.
        builder.HasIndex(m => new { m.ConversationId, m.SequenceNumber }).IsUnique();
        builder.HasIndex(m => new { m.ConversationId, m.IsCompacted });
    }
}
