using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ai.Infrastructure.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(200);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.LastMessageAt).IsRequired();

        // Primary access pattern: "this user's conversations, most recent first".
        builder.HasIndex(c => new { c.UserId, c.LastMessageAt });

        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Summaries)
            .WithOne(s => s.Conversation)
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
